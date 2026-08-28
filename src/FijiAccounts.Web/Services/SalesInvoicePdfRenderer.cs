using FijiAccounts.Domain.Tax;
using FijiAccounts.Web.Data;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace FijiAccounts.Web.Services;

public sealed class SalesInvoicePdfRenderer
{
    private const double PageWidth = 595;
    private const double PageHeight = 842;
    private const double Margin = 42;
    private static readonly object FontLock = new();

    public SalesInvoicePdfRenderer()
    {
        EnsureFontResolver();
    }

    public byte[] Render(SalesInvoice invoice, OrganisationBranding? branding)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        using var document = new PdfDocument();
        document.Info.Title = $"{invoice.InvoiceNumber} - {DocumentTitle(invoice)}";
        document.Info.Author = SupplierName(invoice);

        var regular = new XFont("Invoice Sans", 9, XFontStyleEx.Regular);
        var small = new XFont("Invoice Sans", 8, XFontStyleEx.Regular);
        var bold = new XFont("Invoice Sans", 9, XFontStyleEx.Bold);
        var heading = new XFont("Invoice Sans", 25, XFontStyleEx.Regular);
        var invoiceNumber = new XFont("Invoice Sans", 12, XFontStyleEx.Bold);
        var navy = new XSolidBrush(XColor.FromArgb(7, 59, 76));
        var muted = new XSolidBrush(XColor.FromArgb(102, 121, 133));
        var green = new XSolidBrush(XColor.FromArgb(8, 127, 91));
        var border = new XPen(XColor.FromArgb(223, 232, 236), 0.7);

        var page = AddPage(document);
        var gfx = XGraphics.FromPdfPage(page);
        DrawHeader(gfx, invoice, branding, heading, invoiceNumber, small, navy, green);
        DrawMeta(gfx, invoice, regular, bold, small, muted);

        var y = 255d;
        DrawTableHeader(gfx, y, bold);
        y += 31;

        foreach (var line in invoice.Lines)
        {
            if (y > PageHeight - 150)
            {
                gfx.Dispose();
                page = AddPage(document);
                gfx = XGraphics.FromPdfPage(page);
                DrawContinuationHeader(gfx, invoice, invoiceNumber, small, navy);
                y = 90;
                DrawTableHeader(gfx, y, bold);
                y += 31;
            }

            var detail = LineItemDetail(line);
            var rowHeight = string.IsNullOrWhiteSpace(detail) ? 46d : 58d;
            DrawCell(gfx, line.Description, regular, Margin + 8, y + 10, 205);
            if (!string.IsNullOrWhiteSpace(detail))
                DrawCell(gfx, detail, small, Margin + 8, y + 29, 205);
            DrawRight(gfx, line.Quantity.ToString("N2"), regular, Margin + 278, y + 25);
            DrawRight(gfx, $"{invoice.Currency} {line.TransactionUnitPrice:N2}", regular, Margin + 378, y + 25);
            DrawCell(gfx, FijiTaxDocumentCompliance.TaxLabel(line), regular, Margin + 393, y + 10, 72);
            DrawRight(gfx, $"{invoice.Currency} {line.TransactionNetAmount:N2}", regular, PageWidth - Margin - 8, y + 25);
            gfx.DrawLine(border, Margin, y + rowHeight, PageWidth - Margin, y + rowHeight);
            y += rowHeight;
        }

        y = Math.Max(y + 32, 395);
        if (y > PageHeight - 245)
        {
            gfx.Dispose();
            page = AddPage(document);
            gfx = XGraphics.FromPdfPage(page);
            DrawContinuationHeader(gfx, invoice, invoiceNumber, small, navy);
            y = 105;
        }

        DrawTotals(gfx, invoice, y, regular, bold, navy);
        gfx.Dispose();

        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }

    private static PdfPage AddPage(PdfDocument document)
    {
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(PageWidth);
        page.Height = XUnit.FromPoint(PageHeight);
        return page;
    }

    private static void DrawHeader(
        XGraphics gfx,
        SalesInvoice invoice,
        OrganisationBranding? branding,
        XFont heading,
        XFont invoiceNumber,
        XFont small,
        XBrush navy,
        XBrush green)
    {
        var logoDrawn = false;
        if (branding is not null && branding.LogoContentType is "image/png" or "image/jpeg")
        {
            try
            {
                using var stream = new MemoryStream(
                    branding.LogoContent,
                    0,
                    branding.LogoContent.Length,
                    writable: false,
                    publiclyVisible: true);
                using var image = XImage.FromStream(stream);
                var ratio = Math.Min(125d / image.PixelWidth, 48d / image.PixelHeight);
                gfx.DrawImage(image, Margin, 28, image.PixelWidth * ratio, image.PixelHeight * ratio);
                logoDrawn = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
            {
                // A valid invoice is more important than an unsupported logo encoding.
            }
        }
        if (!logoDrawn)
        {
            gfx.DrawString(SupplierName(invoice), invoiceNumber, green, new XPoint(Margin, 59));
        }

        DrawCentered(gfx, DocumentTitle(invoice), heading, navy, 168, 428, 61);
        DrawRight(gfx, invoice.InvoiceNumber, invoiceNumber, PageWidth - Margin, 49);
        DrawRight(gfx, StatusLabel(invoice.Status), small, PageWidth - Margin, 72, green);
        gfx.DrawLine(new XPen(XColor.FromArgb(7, 59, 76), 2.2), Margin, 98, PageWidth - Margin, 98);
    }

    private static void DrawContinuationHeader(
        XGraphics gfx,
        SalesInvoice invoice,
        XFont invoiceNumber,
        XFont small,
        XBrush navy)
    {
        gfx.DrawString(DocumentTitle(invoice), invoiceNumber, navy, new XPoint(Margin, 48));
        DrawRight(gfx, $"{invoice.InvoiceNumber} (continued)", small, PageWidth - Margin, 48);
        gfx.DrawLine(new XPen(XColor.FromArgb(7, 59, 76), 1.5), Margin, 60, PageWidth - Margin, 60);
    }

    private static void DrawMeta(
        XGraphics gfx,
        SalesInvoice invoice,
        XFont regular,
        XFont bold,
        XFont small,
        XBrush muted)
    {
        const double y = 132;
        gfx.DrawString("FROM", small, muted, new XPoint(Margin, y));
        gfx.DrawString(SupplierName(invoice), bold, XBrushes.Black, new XPoint(Margin, y + 18));
        DrawCell(gfx, invoice.SupplierAddressSnapshot ?? invoice.Organisation.BusinessAddress ?? string.Empty, regular, Margin, y + 36, 175);
        var supplierTin = invoice.SupplierTinSnapshot ?? invoice.Organisation.Tin;
        if (!string.IsNullOrWhiteSpace(supplierTin))
            gfx.DrawString($"TIN: {supplierTin}", regular, XBrushes.Black, new XPoint(Margin, y + 76));

        const double billX = 218;
        gfx.DrawString("BILL TO", small, muted, new XPoint(billX, y));
        gfx.DrawString(invoice.RecipientNameSnapshot ?? invoice.Customer.Name, bold, XBrushes.Black, new XPoint(billX, y + 18));
        DrawCell(gfx, invoice.RecipientAddressSnapshot ?? invoice.Customer.Address ?? string.Empty, regular, billX, y + 36, 125);

        const double labelX = 360;
        const double valueX = PageWidth - Margin;
        DrawPair(gfx, "Issue date", invoice.IssueDate.ToString("dd MMM yyyy"), labelX, valueX, y, regular, muted);
        DrawPair(gfx, "Due date", invoice.DueDate.ToString("dd MMM yyyy"), labelX, valueX, y + 24, regular, muted);
        DrawPair(gfx, "Currency", invoice.Currency, labelX, valueX, y + 48, regular, muted);
        DrawPair(gfx, "Branch / division", $"{invoice.Branch?.Name ?? "-"} / {invoice.Division?.Name ?? "-"}", labelX, valueX, y + 72, regular, muted);
    }

    private static void DrawTableHeader(XGraphics gfx, double y, XFont bold)
    {
        var background = new XSolidBrush(XColor.FromArgb(7, 59, 76));
        gfx.DrawRectangle(background, Margin, y, PageWidth - (Margin * 2), 31);
        var white = XBrushes.White;
        gfx.DrawString("Description", bold, white, new XPoint(Margin + 8, y + 20));
        DrawRight(gfx, "Qty", bold, Margin + 278, y + 20, white);
        DrawRight(gfx, "Unit price", bold, Margin + 378, y + 20, white);
        gfx.DrawString("Tax treatment", bold, white, new XPoint(Margin + 393, y + 20));
        DrawRight(gfx, "VAT excl.", bold, PageWidth - Margin - 8, y + 20, white);
    }

    private static void DrawTotals(
        XGraphics gfx,
        SalesInvoice invoice,
        double y,
        XFont regular,
        XFont bold,
        XBrush navy)
    {
        var x = PageWidth - Margin - 250;
        DrawTotal(gfx, "VAT exclusive", $"{invoice.Currency} {invoice.TransactionSubtotal:N2}", x, y, regular, bold);
        y += 23;

        foreach (var supply in invoice.Lines
                     .Where(line => line.VatTreatment != VatTreatment.Standard)
                     .GroupBy(line => line.VatTreatment))
        {
            var label = $"{FijiTaxDocumentCompliance.TaxLabel(supply.First())} supplies";
            var amount = supply.Sum(line => line.TransactionNetAmount + line.TransactionVatAmount);
            DrawTotal(gfx, label, $"{invoice.Currency} {amount:N2}", x, y, regular, bold);
            y += 23;
        }

        DrawTotal(gfx, invoice.Organisation.TaxLabel, $"{invoice.Currency} {invoice.TransactionVatTotal:N2}", x, y, regular, bold);
        y += 26;
        gfx.DrawLine(new XPen(XColor.FromArgb(23, 50, 77), 1.4), x, y - 17, PageWidth - Margin, y - 17);
        DrawTotal(gfx, "VAT inclusive", $"{invoice.Currency} {invoice.TransactionTotal:N2}", x, y, bold, bold, navy);
        y += 27;
        gfx.DrawLine(new XPen(XColor.FromArgb(23, 50, 77), 1.4), x, y - 17, PageWidth - Margin, y - 17);

        if (!string.Equals(invoice.Currency, invoice.Organisation.BaseCurrency, StringComparison.OrdinalIgnoreCase))
        {
            DrawTotal(gfx, "Exchange rate", $"1 {invoice.Currency} = {invoice.ExchangeRateToBase:0.########} {invoice.Organisation.BaseCurrency}", x, y, regular, bold);
            y += 23;
            DrawTotal(gfx, "Posted value", $"{invoice.Organisation.BaseCurrency} {invoice.Total:N2}", x, y, regular, bold);
            y += 23;
        }

        DrawTotal(gfx, "Credits (base)", $"{invoice.Organisation.BaseCurrency} {invoice.AmountCredited:N2}", x, y, regular, bold);
        y += 23;
        DrawTotal(gfx, "Paid (base)", $"{invoice.Organisation.BaseCurrency} {invoice.AmountPaid:N2}", x, y, regular, bold);
        y += 23;
        DrawTotal(gfx, "Amount due (base)", $"{invoice.Organisation.BaseCurrency} {invoice.Total - invoice.AmountPaid - invoice.AmountCredited:N2}", x, y, bold, bold, navy);
    }

    private static void DrawPair(XGraphics gfx, string label, string value, double x, double right, double y, XFont font, XBrush labelBrush)
    {
        gfx.DrawString(label, font, labelBrush, new XPoint(x, y));
        DrawRight(gfx, value, font, right, y);
    }

    private static void DrawTotal(XGraphics gfx, string label, string value, double x, double y, XFont labelFont, XFont valueFont, XBrush? brush = null)
    {
        brush ??= XBrushes.Black;
        gfx.DrawString(label, labelFont, brush, new XPoint(x, y));
        DrawRight(gfx, value, valueFont, PageWidth - Margin, y, brush);
    }

    private static void DrawRight(XGraphics gfx, string value, XFont font, double right, double baseline, XBrush? brush = null)
    {
        brush ??= XBrushes.Black;
        var width = gfx.MeasureString(value, font).Width;
        gfx.DrawString(value, font, brush, new XPoint(right - width, baseline));
    }

    private static void DrawCentered(XGraphics gfx, string value, XFont font, XBrush brush, double left, double right, double baseline)
    {
        var width = gfx.MeasureString(value, font).Width;
        gfx.DrawString(value, font, brush, new XPoint(left + ((right - left - width) / 2), baseline));
    }

    private static void DrawCell(XGraphics gfx, string value, XFont font, double x, double y, double maxWidth)
    {
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return;

        var line = words[0];
        var baseline = y + font.Size;
        foreach (var word in words.Skip(1))
        {
            var candidate = $"{line} {word}";
            if (gfx.MeasureString(candidate, font).Width <= maxWidth)
            {
                line = candidate;
                continue;
            }

            gfx.DrawString(line, font, XBrushes.Black, new XPoint(x, baseline));
            baseline += font.Size + 2;
            line = word;
        }
        gfx.DrawString(line, font, XBrushes.Black, new XPoint(x, baseline));
    }

    private static string DocumentTitle(SalesInvoice invoice)
    {
        if (invoice.Status == InvoiceStatus.Draft) return "Draft Invoice";
        var isTaxDocument = invoice.IsTaxInvoice ?? invoice.Lines.Any(line =>
            line.VatTreatment is VatTreatment.Standard or VatTreatment.ZeroRated);
        return isTaxDocument ? "Tax Invoice" : "Commercial Invoice";
    }

    private static string SupplierName(SalesInvoice invoice) =>
        invoice.SupplierNameSnapshot ?? invoice.Organisation.LegalName;

    private static string LineItemDetail(SalesInvoiceLine line)
    {
        var parts = new List<string>();
        if (line.Project is not null)
            parts.Add($"Project: {line.Project.ProjectNumber}{(line.ProjectCostCode is null ? string.Empty : $" / {line.ProjectCostCode.Code}")}");
        if (!string.IsNullOrWhiteSpace(line.CustomerPurchaseOrderNumber))
            parts.Add($"Customer PO: {line.CustomerPurchaseOrderNumber}");
        return string.Join(" | ", parts);
    }

    private static string StatusLabel(InvoiceStatus status) => status == InvoiceStatus.PartPaid
        ? "Part paid"
        : status.ToString();

    private static void EnsureFontResolver()
    {
        if (GlobalFontSettings.FontResolver is not null) return;
        lock (FontLock)
        {
            GlobalFontSettings.FontResolver ??= new InvoiceFontResolver();
        }
    }

    private sealed class InvoiceFontResolver : IFontResolver
    {
        private const string RegularFace = "InvoiceSans-Regular";
        private const string BoldFace = "InvoiceSans-Bold";
        private readonly Lazy<byte[]> regular = new(() => File.ReadAllBytes(FindFont(false)));
        private readonly Lazy<byte[]> bold = new(() => File.ReadAllBytes(FindFont(true)));

        public byte[]? GetFont(string faceName) => faceName == BoldFace ? bold.Value : regular.Value;

        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
            new(isBold ? BoldFace : RegularFace);

        private static string FindFont(bool bold)
        {
            var candidates = OperatingSystem.IsWindows()
                ? new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), bold ? "arialbd.ttf" : "arial.ttf"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), bold ? "segoeuib.ttf" : "segoeui.ttf")
                }
                : new[]
                {
                    bold ? "/usr/share/fonts/dejavu/DejaVuSans-Bold.ttf" : "/usr/share/fonts/dejavu/DejaVuSans.ttf",
                    bold ? "/usr/share/fonts/TTF/DejaVuSans-Bold.ttf" : "/usr/share/fonts/TTF/DejaVuSans.ttf",
                    bold ? "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" : "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
                };

            return candidates.FirstOrDefault(File.Exists)
                ?? throw new InvalidOperationException("The invoice PDF font is not installed.");
        }
    }
}
