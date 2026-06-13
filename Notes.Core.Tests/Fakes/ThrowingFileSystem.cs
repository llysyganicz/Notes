using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using NSubstitute;

namespace Notes.Core.Tests.Fakes;

public static class ThrowingFileSystem
{
    /// <summary>
    /// Returns an IFileSystem backed by MockFileSystem that throws on the configured operation,
    /// so fault tests can assert the original file survives without touching real disk.
    /// </summary>
    public static (IFileSystem fs, MockFileSystem inner) Create(
        bool throwOnWriteAllText = false,
        bool throwOnMove = false)
    {
        var inner = new MockFileSystem();

        var fakeFile = Substitute.For<IFile>();
        fakeFile.Exists(Arg.Any<string?>())
            .Returns(ci => inner.File.Exists(ci.ArgAt<string?>(0)));
        fakeFile.ReadAllText(Arg.Any<string>())
            .Returns(ci => inner.File.ReadAllText(ci.ArgAt<string>(0)));
        fakeFile.When(x => x.WriteAllText(Arg.Any<string>(), Arg.Any<string?>()))
            .Do(ci =>
            {
                inner.File.WriteAllText(ci.ArgAt<string>(0), ci.ArgAt<string?>(1));
                if (throwOnWriteAllText)
                    throw new IOException("Simulated fault: WriteAllText");
            });
        fakeFile.When(x => x.Move(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>()))
            .Do(ci =>
            {
                if (throwOnMove)
                    throw new IOException("Simulated fault: Move");
                inner.File.Move(ci.ArgAt<string>(0), ci.ArgAt<string>(1), ci.ArgAt<bool>(2));
            });
        fakeFile.When(x => x.Delete(Arg.Any<string>()))
            .Do(ci => inner.File.Delete(ci.ArgAt<string>(0)));

        var fs = Substitute.For<IFileSystem>();
        fs.File.Returns(fakeFile);
        fs.Path.Returns(inner.Path);

        return (fs, inner);
    }
}
