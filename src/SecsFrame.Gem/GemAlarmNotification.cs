namespace SecsFrame.Gem;

/// <summary>Contains one decoded alarm notification.</summary>
public sealed class GemAlarmNotification
{
    /// <summary>Creates an alarm notification value.</summary>
    public GemAlarmNotification(byte code, SecsItem alarmId, string text)
    {
        Code = code;
        AlarmId = alarmId ?? throw new ArgumentNullException(nameof(alarmId));
        Text = text ?? throw new ArgumentNullException(nameof(text));

        _ = SecsItem.Ascii(text);
    }

    /// <summary>
    /// Gets the exact alarm code byte without interpreting its bit fields.
    /// </summary>
    public byte Code { get; }

    /// <summary>Gets the exact dynamic alarm identifier.</summary>
    public SecsItem AlarmId { get; }

    /// <summary>Gets the seven-bit ASCII alarm text.</summary>
    public string Text { get; }
}
