namespace Trophic.TrophyFormat.Exceptions;

public class InvalidTrophyFileException : Exception
{
    public InvalidTrophyFileException(string message) : base(message) { }
    public InvalidTrophyFileException(string message, Exception inner) : base(message, inner) { }
}

public class TrophyAlreadySyncException : Exception
{
    public int TrophyId { get; }
    public TrophyAlreadySyncException(int trophyId)
        : base($"Trophy {trophyId} is already synced and cannot be modified.")
    {
        TrophyId = trophyId;
    }
}

public class TrophyAlreadyEarnedException : Exception
{
    public int TrophyId { get; }
    public TrophyAlreadyEarnedException(int trophyId)
        : base($"Trophy {trophyId} is already earned.")
    {
        TrophyId = trophyId;
    }
}

public class TrophySyncTimeException : Exception
{
    public TrophySyncTimeException(string message) : base(message) { }
}

public class TrophyNotFoundException : Exception
{
    public int TrophyId { get; }
    public TrophyNotFoundException(int trophyId)
        : base($"Trophy {trophyId} not found.")
    {
        TrophyId = trophyId;
    }
}
