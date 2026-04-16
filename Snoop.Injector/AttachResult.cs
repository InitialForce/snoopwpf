namespace Snoop;

using System;

public class AttachResult
{
    public AttachResult()
    {
        this.Success = true;
    }

    public AttachResult(Exception attachException)
    {
        this.Success = false;

        this.AttachException = attachException;
    }

    public bool Success { get; }

    public Exception? AttachException { get; }
}
