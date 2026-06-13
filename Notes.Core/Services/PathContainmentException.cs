using System.IO;

namespace Notes.Core.Services;

public sealed class PathContainmentException : IOException
{
    public PathContainmentException(string message) : base(message) { }
}
