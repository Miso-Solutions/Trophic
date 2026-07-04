// Resolves a GitHub release asset to a short-lived, unauthenticated download URL.
//
// GitHub's asset API URL requires the PAT via Authorization, but when requested
// with `Accept: application/octet-stream` it responds with a 302 redirect to a
// signed objects.githubusercontent.com URL that is valid for a few minutes and
// needs no auth. We capture that Location and hand it to the client, so the PAT
// never leaves the server and the server does not stream the bytes itself.

const REPO_API_BASE = process.env.TROPHIC_REPO_API
  ?? 'https://api.github.com/repos/Miso-Solutions/Miso-PS3-Database';

function token() {
  const t = process.env.GITHUB_TOKEN;
  if (!t) throw new Error('GITHUB_TOKEN is not configured on the server.');
  return t;
}

// Wraps fetch with an abortable timeout so a hung GitHub call cannot tie up the
// request indefinitely.
async function fetchWithTimeout(url, options, timeoutMs = 12_000) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    return await fetch(url, { ...options, signal: controller.signal });
  } finally {
    clearTimeout(timer);
  }
}

async function findAssetApiUrl(npwrId, zipName) {
  const res = await fetchWithTimeout(`${REPO_API_BASE}/releases/tags/${encodeURIComponent(npwrId)}`, {
    headers: {
      Authorization: `Bearer ${token()}`,
      Accept: 'application/vnd.github+json',
      'User-Agent': 'Trophic-License-Server',
    },
  });
  if (res.status === 404) {
    const err = new Error('release_not_found');
    err.code = 'release_not_found';
    throw err;
  }
  if (!res.ok) {
    const err = new Error(`github_api_${res.status}`);
    err.code = 'github_api_error';
    throw err;
  }
  const data = await res.json();
  const asset = (data.assets ?? []).find((a) => a.name === zipName);
  if (!asset) {
    const err = new Error('asset_not_found');
    err.code = 'asset_not_found';
    throw err;
  }
  return asset.url;
}

/**
 * Returns { url, expiresInSeconds } for a short-lived signed download URL.
 */
export async function resolveDownloadUrl(npwrId) {
  const zipName = `PS3_${npwrId}.zip`;
  const assetApiUrl = await findAssetApiUrl(npwrId, zipName);

  // Manual redirect: GitHub answers the octet-stream Accept with a 302 to the signed URL.
  const res = await fetchWithTimeout(assetApiUrl, {
    method: 'GET',
    redirect: 'manual',
    headers: {
      Authorization: `Bearer ${token()}`,
      Accept: 'application/octet-stream',
      'User-Agent': 'Trophic-License-Server',
    },
  });

  const location = res.headers.get('location');
  if ((res.status === 302 || res.status === 307) && location) {
    // githubusercontent signed URLs are typically valid ~5 minutes.
    return { url: location, expiresInSeconds: 300 };
  }

  const err = new Error(`unexpected_status_${res.status}`);
  err.code = 'github_redirect_failed';
  throw err;
}
