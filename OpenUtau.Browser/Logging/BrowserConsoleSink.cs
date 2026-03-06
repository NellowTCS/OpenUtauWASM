using System;
using System.IO;
using System.Runtime.InteropServices.JavaScript;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace OpenUtau.Browser.Logging;

public class BrowserConsoleSink : ILogEventSink
{
    private readonly IFormatProvider? _formatProvider;

    public BrowserConsoleSink(IFormatProvider? formatProvider)
    {
        _formatProvider = formatProvider;
    }

    public void Emit(LogEvent logEvent)
    {
        var message = logEvent.RenderMessage(_formatProvider);
        if (logEvent.Exception != null)
        {
            message += "\n" + logEvent.Exception.ToString();
        }
        Console.WriteLine($"[{logEvent.Level}] {message}");
    }
}
