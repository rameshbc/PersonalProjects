namespace OfficeScriptWorkflow.Worker.Exceptions;

public class ExcelOperationException : Exception
{
    public string? SheetName { get; }
    public string? TargetName { get; }

    public ExcelOperationException(string message) : base(message) { }

    public ExcelOperationException(string message, string sheetName, string targetName)
        : base(message)
    {
        SheetName = sheetName;
        TargetName = targetName;
    }

    public ExcelOperationException(string message, Exception inner)
        : base(message, inner) { }
}
