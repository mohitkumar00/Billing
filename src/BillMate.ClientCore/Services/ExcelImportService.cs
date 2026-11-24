using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using BillMate.Application.Features.Products;

namespace BillMate.ClientCore.Services;

public static class ExcelImportService
{
    public static string GetSampleCsvTemplate()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Name,SKU,Barcode,SellingPrice,PurchasePrice,MRP,OpeningStock,GST%,Unit,HSN");
        sb.AppendLine("Basmati Rice 5kg,RICE-5KG,8901234567890,450.00,380.00,500.00,25,5,PCS,100630");
        sb.AppendLine("Amul Butter 500g,AMUL-500G,8901234567891,275.00,240.00,285.00,40,12,PCS,040510");
        sb.AppendLine("Tata Tea Gold 1kg,TEA-1KG,8901234567892,540.00,460.00,590.00,30,5,PCS,090240");
        return sb.ToString();
    }

    public static async Task<IReadOnlyList<BulkImportProductItem>> ParseStreamAsync(Stream stream, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext == ".xlsx")
        {
            return await ParseXlsxAsync(stream);
        }

        return await ParseCsvAsync(stream);
    }

    public static async Task<IReadOnlyList<BulkImportProductItem>> ParseCsvAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var lines = new List<string[]>();

        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            lines.Add(ParseCsvLine(line));
        }

        return MapRowsToProducts(lines);
    }

    public static Task<IReadOnlyList<BulkImportProductItem>> ParseXlsxAsync(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        // 1. Read shared strings if present
        var sharedStrings = new List<string>();
        var sharedStringsEntry = zip.GetEntry("xl/sharedStrings.xml");
        if (sharedStringsEntry != null)
        {
            using var ssStream = sharedStringsEntry.Open();
            var ssDoc = XDocument.Load(ssStream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            foreach (var si in ssDoc.Descendants(ns + "si"))
            {
                var text = string.Concat(si.Descendants(ns + "t").Select(t => t.Value));
                sharedStrings.Add(text);
            }
        }

        // 2. Read first sheet
        var sheetEntry = zip.GetEntry("xl/worksheets/sheet1.xml") ?? zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/sheet") && e.FullName.EndsWith(".xml"));
        if (sheetEntry == null)
        {
            return Task.FromResult<IReadOnlyList<BulkImportProductItem>>([]);
        }

        using var sheetStream = sheetEntry.Open();
        var sheetDoc = XDocument.Load(sheetStream);
        XNamespace sheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var rows = new List<string[]>();
        foreach (var rowEl in sheetDoc.Descendants(sheetNs + "row"))
        {
            var cellDict = new SortedDictionary<int, string>();
            foreach (var cellEl in rowEl.Elements(sheetNs + "c"))
            {
                var cellRef = cellEl.Attribute("r")?.Value ?? string.Empty;
                var colIdx = GetColumnIndex(cellRef);
                var cellType = cellEl.Attribute("t")?.Value;
                var valEl = cellEl.Element(sheetNs + "v");
                var rawVal = valEl?.Value ?? string.Empty;

                string val;
                if (cellType == "s" && int.TryParse(rawVal, out var sIdx) && sIdx < sharedStrings.Count)
                {
                    val = sharedStrings[sIdx];
                }
                else
                {
                    val = rawVal;
                }

                cellDict[colIdx] = val;
            }

            if (cellDict.Count > 0)
            {
                var maxCol = cellDict.Keys.Max();
                var rowArr = new string[maxCol + 1];
                for (int c = 0; c <= maxCol; c++)
                {
                    rowArr[c] = cellDict.TryGetValue(c, out var v) ? v : string.Empty;
                }
                rows.Add(rowArr);
            }
        }

        return Task.FromResult(MapRowsToProducts(rows));
    }

    private static int GetColumnIndex(string cellRef)
    {
        int col = 0;
        foreach (char ch in cellRef)
        {
            if (char.IsLetter(ch))
            {
                col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            }
            else
            {
                break;
            }
        }
        return Math.Max(0, col - 1);
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        result.Add(sb.ToString().Trim());
        return result.ToArray();
    }

    private static IReadOnlyList<BulkImportProductItem> MapRowsToProducts(IReadOnlyList<string[]> rows)
    {
        if (rows.Count < 2) return [];

        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToArray();
        int nameIdx = FindHeaderIndex(header, "name", "item name", "product name", "item", "title");
        int skuIdx = FindHeaderIndex(header, "sku", "code", "item code");
        int barcodeIdx = FindHeaderIndex(header, "barcode", "upc", "ean");
        int sellingPriceIdx = FindHeaderIndex(header, "selling price", "price", "sellingprice", "rate", "sale price");
        int purchasePriceIdx = FindHeaderIndex(header, "purchase price", "cost", "purchaseprice", "cost price", "buy price");
        int mrpIdx = FindHeaderIndex(header, "mrp", "maximum retail price");
        int stockIdx = FindHeaderIndex(header, "opening stock", "stock", "quantity", "qty", "openingstock");
        int unitIdx = FindHeaderIndex(header, "unit", "uom");
        int taxIdx = FindHeaderIndex(header, "taxratepercent", "gst rate", "tax rate", "gst%", "tax%", "gst percent", "tax percent", "gst", "tax");
        int hsnIdx = FindHeaderIndex(header, "hsn", "hsn/sac", "hsn code", "sac");

        if (nameIdx == -1)
        {
            // If first column or second column has names
            nameIdx = 0;
        }

        var list = new List<BulkImportProductItem>();
        for (int i = 1; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.Length <= nameIdx) continue;

            var name = r[nameIdx].Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            var sku = skuIdx >= 0 && skuIdx < r.Length ? r[skuIdx].Trim() : null;
            var barcode = barcodeIdx >= 0 && barcodeIdx < r.Length ? r[barcodeIdx].Trim() : null;
            var sellingPrice = sellingPriceIdx >= 0 && sellingPriceIdx < r.Length ? ParseDecimal(r[sellingPriceIdx]) : 0m;
            var purchasePrice = purchasePriceIdx >= 0 && purchasePriceIdx < r.Length ? ParseDecimal(r[purchasePriceIdx]) : 0m;
            var mrp = mrpIdx >= 0 && mrpIdx < r.Length ? ParseDecimal(r[mrpIdx]) : sellingPrice;
            var stock = stockIdx >= 0 && stockIdx < r.Length ? ParseDecimal(r[stockIdx]) : 0m;
            var unit = unitIdx >= 0 && unitIdx < r.Length && !string.IsNullOrWhiteSpace(r[unitIdx]) ? r[unitIdx].Trim().ToUpperInvariant() : "PCS";
            var taxRate = taxIdx >= 0 && taxIdx < r.Length ? ParseDecimal(r[taxIdx]) : 0m;
            var hsn = hsnIdx >= 0 && hsnIdx < r.Length ? r[hsnIdx].Trim() : null;

            list.Add(new BulkImportProductItem(
                Name: name,
                Sku: string.IsNullOrWhiteSpace(sku) ? null : sku,
                Barcode: string.IsNullOrWhiteSpace(barcode) ? null : barcode,
                SellingPrice: sellingPrice,
                PurchasePrice: purchasePrice,
                Mrp: mrp,
                OpeningStock: stock,
                Unit: unit,
                TaxRatePercent: taxRate,
                HsnSacCode: string.IsNullOrWhiteSpace(hsn) ? null : hsn));
        }

        return list;
    }

    private static int FindHeaderIndex(string[] headers, params string[] targets)
    {
        // 1. Exact match first
        for (int i = 0; i < headers.Length; i++)
        {
            var h = headers[i].Replace("_", " ").Trim();
            foreach (var t in targets)
            {
                if (string.Equals(h, t, StringComparison.OrdinalIgnoreCase)) return i;
            }
        }

        // 2. Substring match, avoiding spurious substring collisions on short abbreviations
        for (int i = 0; i < headers.Length; i++)
        {
            var h = headers[i].Replace("_", " ").Trim();
            foreach (var t in targets)
            {
                if (t.Length <= 3)
                {
                    if (System.Text.RegularExpressions.Regex.IsMatch(h, $@"\b{System.Text.RegularExpressions.Regex.Escape(t)}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                        h.StartsWith(t, StringComparison.OrdinalIgnoreCase) ||
                        h.EndsWith(t, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
                else if (h.Contains(t, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }
        return -1;
    }

    private static decimal ParseDecimal(string val)
    {
        if (string.IsNullOrWhiteSpace(val)) return 0m;
        val = val.Replace("₹", "").Replace("$", "").Replace(",", "").Trim();
        return decimal.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }
}
