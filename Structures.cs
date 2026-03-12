using OutSystems.ExternalLibraries.SDK;

namespace PdfStamperLibrary;

/// <summary>
/// Result returned by the StampPDF action.
/// </summary>
[OSStructure(Description = "Result from a PDF stamping operation")]
public struct StampResult
{
    [OSStructureField(
        Description = "True if stamping succeeded",
        DataType = OSDataType.Boolean,
        IsMandatory = true)]
    public bool Success;

    [OSStructureField(
        Description = "Success or error message",
        DataType = OSDataType.Text,
        IsMandatory = true)]
    public string Message;

    [OSStructureField(
        Description = "The filled PDF as binary data. Pass to a Download action.",
        DataType = OSDataType.BinaryData,
        IsMandatory = false)]
    public byte[] FilledPdf;

    [OSStructureField(
        Description = "Number of fields stamped onto the PDF",
        DataType = OSDataType.Integer,
        IsMandatory = false)]
    public int FieldsStamped;

    [OSStructureField(
        Description = "Execution log. Minimal on success, verbose on failure.",
        DataType = OSDataType.Text,
        IsMandatory = false)]
    public string DetailedLog;
}

/// <summary>
/// Result returned by GetPDFInfo — used by the Field Mapper to know
/// how many pages the template has before rendering.
/// </summary>
[OSStructure(Description = "Basic metadata about a PDF document")]
public struct PDFInfoResult
{
    [OSStructureField(
        Description = "True if the PDF was read successfully",
        DataType = OSDataType.Boolean,
        IsMandatory = true)]
    public bool Success;

    [OSStructureField(
        Description = "Number of pages in the PDF",
        DataType = OSDataType.Integer,
        IsMandatory = false)]
    public int PageCount;

    [OSStructureField(
        Description = "Width of page 1 in PDF points (1pt = 1/72 inch)",
        DataType = OSDataType.Decimal,
        IsMandatory = false)]
    public decimal PageWidth;

    [OSStructureField(
        Description = "Height of page 1 in PDF points",
        DataType = OSDataType.Decimal,
        IsMandatory = false)]
    public decimal PageHeight;

    [OSStructureField(
        Description = "Error message if Success is false",
        DataType = OSDataType.Text,
        IsMandatory = false)]
    public string Message;
}

/// <summary>
/// Result returned by ExtractTextBlocks.
/// TextBlocksJson is a JSON array: [{text, page, x, y, width, height}, ...]
/// Page is 0-indexed. X/Y use PDF bottom-left origin — same as stamper coordinates.
/// </summary>
[OSStructure(Description = "Result from PDF text extraction — use TextBlocksJson to search for field label positions")]
public struct ExtractionResult
{
    [OSStructureField(
        Description = "True if extraction succeeded",
        DataType = OSDataType.Boolean,
        IsMandatory = true)]
    public bool Success;

    [OSStructureField(
        Description = "Status message or error details",
        DataType = OSDataType.Text,
        IsMandatory = true)]
    public string Message;

    [OSStructureField(
        Description = "JSON array of text blocks: [{\"text\":\"Name of Issuer\",\"page\":0,\"x\":95,\"y\":567,\"width\":72,\"height\":10}]. Page is 0-indexed. X/Y match the coordinate system used by StampPDF.",
        DataType = OSDataType.Text,
        IsMandatory = false)]
    public string TextBlocksJson;

    [OSStructureField(
        Description = "Number of pages in the PDF",
        DataType = OSDataType.Integer,
        IsMandatory = false)]
    public int PageCount;

    [OSStructureField(
        Description = "Total number of text blocks extracted",
        DataType = OSDataType.Integer,
        IsMandatory = false)]
    public int BlockCount;
}
