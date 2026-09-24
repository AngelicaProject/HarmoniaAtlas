using HarmoniaAtlas.Hxs;

namespace HarmoniaAtlas.Game;

/// <summary>
/// A game sheet that cannot be read into the HXS model. Extraction records the
/// sheet as excluded instead of failing the whole snapshot.
/// </summary>
public sealed class SheetReadException : Exception
{
    public SheetReadException(string sheetName, HxsSheetExclusionReason reason, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        SheetName = sheetName;
        Reason = reason;
    }

    public string SheetName { get; }

    public HxsSheetExclusionReason Reason { get; }

    /// <summary>
    /// Returns whether an exception raised while reading game data may be
    /// attributed to one sheet. Cancellation and resource exhaustion never are.
    /// </summary>
    public static bool IsSheetScoped(Exception exception) =>
        exception is not (OperationCanceledException or OutOfMemoryException or StackOverflowException);
}
