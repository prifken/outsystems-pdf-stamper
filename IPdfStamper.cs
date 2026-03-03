using OutSystems.ExternalLibraries.SDK;

namespace PdfStamperLibrary;

/// <summary>
/// OutSystems ODC External Library for stamping data onto flat (non-AcroForm) PDF templates.
///
/// Usage pattern:
///   1. Store your blank PDF template as a binary resource in ODC.
///   2. Build a field map JSON using the companion Field Mapper tool.
///   3. Collect form values in your ODC app as a JSON object.
///   4. Call StampPDF → get back the filled PDF binary → trigger download.
///
/// Field Map JSON format (array of field definitions):
///   [
///     { "field": "firstName", "page": 0, "x": 95,  "y": 567, "type": "text",     "maxWidth": 150, "fontSize": 9 },
///     { "field": "isLLC",     "page": 0, "x": 419, "y": 541, "type": "checkbox"                                 }
///   ]
///   - page:     0-based page index
///   - x, y:     PDF coordinate space (origin = bottom-left, y increases upward)
///   - type:     "text" or "checkbox"
///   - maxWidth: (text only) max width in points before wrapping
///   - fontSize: (text only) font size in points, default 9
///
/// Values JSON format (key-value object):
///   { "firstName": "Jane Smith", "isLLC": "true", "dateOfBirth": "01/01/1990" }
///   - Checkbox fields: "true" / "false" (string)
///   - All other fields: string value to stamp
/// </summary>
[OSInterface(
    Name = "PdfStamper",
    Description = "Stamp form data onto flat PDF templates at precise coordinates. Supports text fields and checkboxes across any number of pages. Works with any flat PDF — government forms, contracts, NDAs, etc.",
    IconResourceName = "PdfStamperLibrary.icon.png")]
public interface IPdfStamper
{
    /// <summary>
    /// Stamps field values onto a flat PDF template at coordinates defined by a field map.
    /// Returns the filled PDF as binary data ready for download.
    /// </summary>
    [OSAction(
        Description = "Stamp form values onto a flat PDF template. Pass the blank PDF binary, a field map JSON defining where each field sits on the page, and a values JSON with the data to stamp. Returns the filled PDF as binary.",
        ReturnName = "result",
        ReturnDescription = "Stamping result including the filled PDF binary and status")]
    StampResult StampPDF(

        [OSParameter(
            Description = "Binary content of the blank PDF template. Store as a Resource in your ODC app or retrieve from your database.",
            DataType = OSDataType.BinaryData)]
        byte[] templatePdf,

        [OSParameter(
            Description = "JSON array defining field locations. Format: [{\"field\":\"firstName\",\"page\":0,\"x\":95,\"y\":567,\"type\":\"text\",\"maxWidth\":150,\"fontSize\":9}]. Generate using the Field Mapper tool.",
            DataType = OSDataType.Text)]
        string fieldMapJson,

        [OSParameter(
            Description = "JSON object mapping field names to values. Format: {\"firstName\":\"Jane Smith\",\"isLLC\":\"true\"}. Checkbox fields use \"true\"/\"false\".",
            DataType = OSDataType.Text)]
        string valuesJson,

        [OSParameter(
            Description = "Ink color as R,G,B values between 0 and 1. Default \"0,0,0\" (black). Use \"0,0,0.55\" for dark blue.",
            DataType = OSDataType.Text)]
        string inkColor
    );

    /// <summary>
    /// Returns page count and dimensions for a PDF. Used by the Field Mapper
    /// to know how many pages to render and the coordinate bounds per page.
    /// </summary>
    [OSAction(
        Description = "Get page count and dimensions of a PDF. Useful before building a field map — tells you how many pages exist and the coordinate space size (typically 612x792 for US Letter).",
        ReturnName = "result",
        ReturnDescription = "PDF metadata including page count and page 1 dimensions")]
    PDFInfoResult GetPDFInfo(

        [OSParameter(
            Description = "Binary content of the PDF to inspect.",
            DataType = OSDataType.BinaryData)]
        byte[] pdfData
    );

    /// <summary>
    /// Returns build version. Required to force new ODC revisions on every CI/CD deploy.
    /// The BUILD_METADATA_PLACEHOLDER string is replaced by GitHub Actions before compilation.
    /// </summary>
    [OSAction(
        Description = "Returns the library build version and deployment metadata. Used to verify which version is running in your ODC environment.",
        ReturnName = "version",
        ReturnDescription = "Build version string including deploy timestamp and commit SHA")]
    string GetBuildVersion();
}
