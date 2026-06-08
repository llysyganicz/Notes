using System.IO;

namespace Notes.Services;

public sealed class PathContainmentException : IOException
{
    public PathContainmentException(string message) : base(message) { }
}
