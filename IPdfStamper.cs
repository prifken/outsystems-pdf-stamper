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
    /// Stamps a PDF using a single list of FieldInstance records (each carrying both
    /// coordinate metadata and a Value). Pass JSON Serialize(Fields) directly from ODC —
    /// no separate ValuesJson or string concatenation required.
    /// </summary>
    [OSAction(
        Description = "Stamp a PDF using a serialized List of FieldInstance records. Each record carries both the field coordinates and its value. Pass JSON Serialize(Fields) directly from OutSystems — no separate ValuesJson needed.",
        ReturnName = "result",
        ReturnDescription = "Stamping result including the filled PDF binary and status")]
    StampResult StampPDFFromList(

        [OSParameter(
            Description = "Binary content of the blank PDF template.",
            DataType = OSDataType.BinaryData)]
        byte[] templatePdf,

        [OSParameter(
            Description = "JSON array of FieldInstance records. Each record must have: Field, Page, X, Y, Type, MaxWidth, FontSize, Value. Pass JSON Serialize(Fields) directly from OutSystems.",
            DataType = OSDataType.Text)]
        string fieldInstancesJson,

        [OSParameter(
            Description = "Ink color as R,G,B values between 0 and 1. Default \"0,0,0\" (black). Use \"0,0,0.55\" for dark blue.",
            DataType = OSDataType.Text)]
        string inkColor
    );

    /// <summary>
    /// Extracts all text words from a PDF with their bounding box coordinates.
    /// Use this to discover field label positions for any new form — equivalent to
    /// running find_coords.py. Search TextBlocksJson for a label name to get its x/y,
    /// then offset slightly to place your stamp target.
    /// </summary>
    [OSAction(
        Description = "Extract all text blocks with bounding box coordinates from a PDF. Returns a JSON array of {text, page, x, y, width, height}. Use to auto-discover field positions for any new form.",
        ReturnName = "result",
        ReturnDescription = "Extraction result with TextBlocksJson array, page count, and block count")]
    ExtractionResult ExtractTextBlocks(

        [OSParameter(
            Description = "Binary content of the PDF to extract text from",
            DataType = OSDataType.BinaryData)]
        byte[] pdfData,

        [OSParameter(
            Description = "Only extract from this page (0-indexed). Pass -1 to extract all pages.",
            DataType = OSDataType.Integer)]
        int pageFilter,

        [OSParameter(
            Description = "Only return blocks containing this text (case-insensitive). Leave empty to return all blocks.",
            DataType = OSDataType.Text)]
        string textFilter
    );

    /// <summary>
    /// Returns width and height in PDF points for every page in the document.
    /// Used by the visual field mapper to know the coordinate bounds for each page.
    /// GetPDFInfo returns page 1 only — use this when the PDF has fields on multiple pages.
    /// </summary>
    [OSAction(
        Description = "Returns dimensions (width and height in PDF points) for every page. Used by the field mapper canvas to correctly scale and position field markers across all pages.",
        ReturnName = "result",
        ReturnDescription = "JSON string: {pageCount, pages:[{page, widthPts, heightPts}]}. Page is 0-indexed.")]
    string GetAllPageDimensions(

        [OSParameter(
            Description = "Binary content of the PDF to inspect.",
            DataType = OSDataType.BinaryData)]
        byte[] pdfData
    );

    /// <summary>
    /// Extracts form fields from a PDF using geometric spatial correlation.
    /// Supports four detection patterns:
    ///   A — bordered rectangle input fields (e.g. SF-270, I-9)
    ///   B — underline fields (e.g. Form D)
    ///   C — unicode checkbox glyphs ☐ ☑ in the text stream (e.g. Form D)
    ///   D — geometric drawn checkbox squares (e.g. SF-270, I-9)
    /// Returns a JSON array of detected fields. Pass the result to an agent that
    /// assigns machine keys (fieldKey) and section labels — the agent must NOT
    /// modify coordinate values. Then save to FormTemplate + FormField records.
    /// Coordinates are PDF-native (bottom-left origin, Y increases upward, points).
    /// </summary>
    [OSAction(
        Description = "Extract form fields from a PDF using geometric spatial correlation — no LLM coordinate guessing. Detects underlines, bordered rectangles, unicode checkboxes (☐ ☑), and drawn checkbox squares. Returns JSON array for agent key-assignment then visual calibration.",
        ReturnName = "result",
        ReturnDescription = "JSON array: [{label, fieldType, page, x0, y0, x1, y1, width, height, confidence, pattern, section}]. Coordinates are PDF-native (bottom-left origin, Y up, points). Page is 0-indexed. fieldType: text_input | checkbox | signature | date | currency | phone | zip_code.")]
    string ExtractFormFields(

        [OSParameter(
            Description = "Binary content of the blank PDF template to analyze.",
            DataType = OSDataType.BinaryData)]
        byte[] pdfData,

        [OSParameter(
            Description = "Only extract from this page (0-indexed). Pass -1 to extract all pages.",
            DataType = OSDataType.Integer)]
        int pageFilter
    );

    /// <summary>
    /// Detects whether a PDF has AcroForm interactive fields or is a flat PDF.
    /// Call this before deciding whether to use FillAcroForm or StampPDFFromList.
    /// AcroForm PDFs (e.g. SF-270) must be filled via FillAcroForm — stamping via
    /// content stream puts text underneath the interactive field layer.
    /// Flat PDFs (e.g. Form D) have no AcroForm layer and must use StampPDFFromList.
    /// </summary>
    [OSAction(
        Description = "Detect whether a PDF has AcroForm interactive fields (formType: 'acroform') or is a flat PDF (formType: 'flat'). AcroForm PDFs must be filled with FillAcroForm. Flat PDFs use StampPDFFromList. Also returns the list of all AcroForm field names and positions for admin mapping.",
        ReturnName = "result",
        ReturnDescription = "JSON: {formType, hasAcroForm, fieldCount, fields:[{name, fieldType, page, x0, y0, x1, y1, width, height, currentValue}]}. formType is 'acroform' or 'flat'.")]
    string DetectFormType(

        [OSParameter(
            Description = "Binary content of the PDF to inspect.",
            DataType = OSDataType.BinaryData)]
        byte[] pdfData
    );

    /// <summary>
    /// Fills AcroForm fields natively in a PDF. Use when DetectFormType returns 'acroform'.
    /// Optionally flattens the form (burns values into page content, removes interactive layer).
    /// Do NOT use this on flat PDFs — use StampPDFFromList instead.
    /// </summary>
    [OSAction(
        Description = "Fill AcroForm interactive fields in a PDF. Use when DetectFormType returns 'acroform'. Supports text, checkbox, dropdown, and signature fields. Set Flatten=True to burn values into the page (non-editable final output) or False to keep the form editable.",
        ReturnName = "result",
        ReturnDescription = "StampResult with FilledPdf binary. On success, FieldsStamped = number of fields filled. DetailedLog lists each field outcome.")]
    StampResult FillAcroForm(

        [OSParameter(
            Description = "Binary content of the blank AcroForm PDF template.",
            DataType = OSDataType.BinaryData)]
        byte[] templatePdf,

        [OSParameter(
            Description = "JSON object mapping AcroForm field names to values. Use field names from DetectFormType. Checkbox values: 'true'/'false'. Text values: any string.",
            DataType = OSDataType.Text)]
        string valuesJson,

        [OSParameter(
            Description = "True = flatten form (burn values into page, non-editable final output). False = keep form editable (user can still modify values after download).",
            DataType = OSDataType.Boolean)]
        bool flatten
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
