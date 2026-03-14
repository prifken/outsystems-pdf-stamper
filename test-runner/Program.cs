using System.Text.Json;
using PdfStamperLibrary;

// ── Resolve PDF path ──────────────────────────────────────────────────────────
var pdfPath = args.Length > 0
    ? args[0]
    : Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        @"Documents\GitHub\outsystems_solutions_architect\projects\os-ui-prototyper\pocs\pdf-stamper\Recommendations\pdf-stamper-upgrade-package\SF270_FILLED_MidAtlantic_Q3_2025.pdf");

if (!File.Exists(pdfPath))
{
    Console.WriteLine($"ERROR: PDF not found at:\n  {pdfPath}");
    Console.WriteLine("\nUsage: dotnet run -- <path-to-pdf.pdf>");
    return 1;
}

Console.WriteLine($"PDF : {Path.GetFileName(pdfPath)}  ({new FileInfo(pdfPath).Length / 1024} KB)");
Console.WriteLine();

var pdfBytes = File.ReadAllBytes(pdfPath);
var stamper  = new PdfStamper();

// ── 1. DetectFormType ─────────────────────────────────────────────────────────
Console.WriteLine("── DetectFormType ──────────────────────────────────────────");
var sw       = System.Diagnostics.Stopwatch.StartNew();
var typeJson = stamper.DetectFormType(pdfBytes);
sw.Stop();
Console.WriteLine($"Done in {sw.ElapsedMilliseconds} ms");

var typeResult = JsonSerializer.Deserialize<JsonElement>(typeJson);
var formType   = typeResult.GetProperty("formType").GetString();
var acroCount  = typeResult.GetProperty("fieldCount").GetInt32();
Console.WriteLine($"formType   : {formType}");
Console.WriteLine($"fieldCount : {acroCount}");

if (formType == "acroform")
{
    Console.WriteLine("\nSample AcroForm fields (first 10):");
    Console.WriteLine($"{"Name",-40} {"Type",-12} {"Pg",-4} BBox");
    Console.WriteLine(new string('─', 90));
    foreach (var f in typeResult.GetProperty("fields").EnumerateArray().Take(10))
    {
        var name = (f.GetProperty("name").GetString() ?? "").PadRight(40);
        if (name.Length > 40) name = name[..39] + "…";
        var ft   = (f.GetProperty("fieldType").GetString() ?? "").PadRight(12);
        var pg   = f.GetProperty("page").GetInt32();
        var x0   = f.GetProperty("x0").GetDouble();
        var y0   = f.GetProperty("y0").GetDouble();
        var x1   = f.GetProperty("x1").GetDouble();
        var y1   = f.GetProperty("y1").GetDouble();
        Console.WriteLine($"{name} {ft} {pg,-4} ({x0:F0},{y0:F0})→({x1:F0},{y1:F0})");
    }
    if (acroCount > 10)
        Console.WriteLine($"  ... and {acroCount - 10} more");
}
Console.WriteLine();

// ── 2. ExtractFormFields ──────────────────────────────────────────────────────
Console.WriteLine("── ExtractFormFields ───────────────────────────────────────");
sw.Restart();
var json = stamper.ExtractFormFields(pdfBytes, -1);
sw.Stop();
Console.WriteLine($"Done in {sw.ElapsedMilliseconds} ms\n");

// ── Parse JSON ────────────────────────────────────────────────────────────────
List<JsonElement> fields;
try
{
    fields = JsonSerializer.Deserialize<List<JsonElement>>(json)
             ?? throw new Exception("Deserialize returned null");
}
catch (Exception ex)
{
    Console.WriteLine($"ERROR parsing result JSON: {ex.Message}");
    Console.WriteLine("Raw output (first 500 chars):");
    Console.WriteLine(json.Length > 500 ? json[..500] : json);
    return 2;
}

// ── Summary ───────────────────────────────────────────────────────────────────
Console.WriteLine($"Total fields detected : {fields.Count}");
Console.WriteLine();

PrintGroup("By pattern",    fields, f => f.GetProperty("pattern").GetString()   ?? "?");
PrintGroup("By field type", fields, f => f.GetProperty("fieldType").GetString() ?? "?");
PrintGroup("By page",       fields, f => $"Page {f.GetProperty("page").GetInt32()}");
PrintGroup("Confidence",    fields, f => f.GetProperty("confidence").GetString() ?? "?");

// ── Field listing ─────────────────────────────────────────────────────────────
int listCount = Math.Min(fields.Count, 30);
Console.WriteLine($"First {listCount} fields:\n");
Console.WriteLine($"{"Pg",-3} {"Pattern",-18} {"Type",-12} {"Conf",-7} {"Label",-42} BBox");
Console.WriteLine(new string('─', 120));

foreach (var f in fields.Take(listCount))
{
    var pg      = f.GetProperty("page").GetInt32();
    var pattern = (f.GetProperty("pattern").GetString()   ?? "").PadRight(18)[..18];
    var type    = (f.GetProperty("fieldType").GetString() ?? "").PadRight(12)[..12];
    var conf    = (f.GetProperty("confidence").GetString() ?? "").PadRight(7)[..7];
    var rawLabel = f.GetProperty("label").GetString() ?? "";
    var label   = rawLabel.Length > 40 ? rawLabel[..39] + "…" : rawLabel.PadRight(42);
    var x0 = f.GetProperty("x0").GetDouble();
    var y0 = f.GetProperty("y0").GetDouble();
    var x1 = f.GetProperty("x1").GetDouble();
    var y1 = f.GetProperty("y1").GetDouble();

    Console.WriteLine($"{pg,-3} {pattern} {type} {conf} {label} ({x0:F0},{y0:F0})→({x1:F0},{y1:F0})");
}

if (fields.Count > listCount)
    Console.WriteLine($"  ... and {fields.Count - listCount} more");

// ── Save full output ──────────────────────────────────────────────────────────
var outPath = Path.Combine(
    Path.GetDirectoryName(pdfPath)!,
    Path.GetFileNameWithoutExtension(pdfPath) + "_extracted.json");

File.WriteAllText(outPath, JsonSerializer.Serialize(
    JsonSerializer.Deserialize<JsonElement>(json),
    new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine($"\nFull JSON saved to:\n  {outPath}");
return 0;

// ── Helpers ───────────────────────────────────────────────────────────────────
static void PrintGroup(string title, List<JsonElement> fields, Func<JsonElement, string> key)
{
    Console.WriteLine($"{title}:");
    foreach (var g in fields.GroupBy(key).OrderByDescending(g => g.Count()))
        Console.WriteLine($"  {g.Key,-22} {g.Count(),4}");
    Console.WriteLine();
}
