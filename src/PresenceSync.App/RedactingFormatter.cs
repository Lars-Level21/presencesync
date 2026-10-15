using PresenceSync.Core.Diagnostics;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace PresenceSync.App;

/// <summary>Renders log lines with the normal template, then redacts e-mail addresses and tokens (incl. exception text).</summary>
internal sealed class RedactingFormatter(string outputTemplate) : ITextFormatter
{
    private readonly MessageTemplateTextFormatter _inner = new(outputTemplate);

    public void Format(LogEvent logEvent, TextWriter output)
    {
        using var buffer = new StringWriter();
        _inner.Format(logEvent, buffer);
        output.Write(LogRedaction.Redact(buffer.ToString()));
    }
}
