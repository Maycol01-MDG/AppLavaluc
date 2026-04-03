using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AppLavaluc.Models;
using Microsoft.Extensions.Configuration;

namespace AppLavaluc.Services
{
    public sealed class EscPosTicketPrinter
    {
        private readonly string _primaryPrinterName;
        private readonly string _configuredPrinterName;
        private readonly int _copies;

        public EscPosTicketPrinter(IConfiguration configuration)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _configuredPrinterName = (configuration["ThermalPrinter:Name"] ?? "").Trim();
            if (!int.TryParse(configuration["ThermalPrinter:Copies"], out _copies) || _copies < 1)
            {
                _copies = 2;
            }

            var selected = _configuredPrinterName;
            if (string.IsNullOrWhiteSpace(selected) && RawPrinterHelper.TryGetDefaultPrinterName(out var defaultPrinter))
            {
                selected = (defaultPrinter ?? "").Trim();
            }

            if (string.IsNullOrWhiteSpace(selected))
            {
                selected = "XP-80T";
            }

            _primaryPrinterName = selected;
        }
        

        public bool TryPrintOrder(Orden orden, out string? error)
        {
            error = null;

            if (orden == null)
            {
                error = "Orden nula.";
                return false;
            }

            var bytes = BuildTicketBytes(orden);
            if (RawPrinterHelper.TrySendBytes(_primaryPrinterName, bytes, out error))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(_configuredPrinterName) && RawPrinterHelper.TryGetDefaultPrinterName(out var defaultPrinterName))
            {
                var defaultName = (defaultPrinterName ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(defaultName) && !string.Equals(defaultName, _primaryPrinterName, StringComparison.OrdinalIgnoreCase))
                {
                    if (RawPrinterHelper.TrySendBytes(defaultName, bytes, out var error2))
                    {
                        error = null;
                        return true;
                    }

                    error = string.IsNullOrWhiteSpace(error2) ? error : $"{error} | FallbackDefault: {error2}";
                }
            }

            return false;
        }

        private byte[] BuildTicketBytes(Orden orden)
        {
            var enc = Encoding.GetEncoding(850);
            var buffer = new List<byte>(4096);

            var copies = Math.Clamp(_copies, 1, 5);
            for (var i = 0; i < copies; i++)
            {
                var label = i == 0 ? "COPIA CLIENTE" : "COPIA REGISTRO";
                AppendTicket(buffer, enc, orden, label);
            }

            return buffer.ToArray();
        }

        private static void AppendTicket(List<byte> buffer, Encoding enc, Orden orden, string copyLabel)
        {
            void Add(params byte[] b) => buffer.AddRange(b);
            void AddText(string s) => buffer.AddRange(enc.GetBytes(s));
            void AddLine(string s = "")
            {
                AddText(s);
                Add(0x0A);
            }

            int lineWidth = 48;
            int qtyWidth = 5;
            int totalWidth = 11;
            int nameWidth = Math.Max(10, lineWidth - qtyWidth - totalWidth);

            Add(0x1B, 0x40);
            Add(0x1B, 0x74, 0x02);

            Add(0x1B, 0x61, 0x01);
            Add(0x1B, 0x45, 0x01);
            AddLine(copyLabel);
            AddLine("APP LAVALUC");
            Add(0x1B, 0x45, 0x00);
            AddLine("Av. Principal 123, Centro");
            AddLine("Tel: (01) 999-999-999");
            AddLine("RUC: 10123456789");

            AddLine(new string('-', lineWidth));

            Add(0x1B, 0x61, 0x00);
            var ordenNum = orden.OrdenID.ToString("D6", CultureInfo.InvariantCulture);
            var fecha = orden.FechaRecepcion.ToString("dd/MM/yy HH:mm", CultureInfo.InvariantCulture);
            AddLine(TwoColumns($"ORDEN: #{ordenNum}", fecha, lineWidth));

            AddLine(new string('-', lineWidth));

            var cliente = orden.Cliente?.NombreCompleto ?? "Cliente";
            Add(0x1B, 0x45, 0x01);
            foreach (var l in Wrap(cliente, lineWidth))
            {
                AddLine(l);
            }
            Add(0x1B, 0x45, 0x00);

            var tel = string.IsNullOrWhiteSpace(orden.Telefono) ? (orden.Cliente?.Telefono ?? "--") : orden.Telefono;
            AddLine($"Tel: {tel}");
            AddLine($"Entrega: {orden.TipoEntrega ?? "--"}");
            AddLine($"Fecha Est.: {(orden.FechaEntregaEstimada?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Por confirmar")}");

            AddLine(new string('-', lineWidth));

            AddLine($"{PadRight("DESCRIPCIÓN", nameWidth)}{PadLeft("CANT", qtyWidth)}{PadLeft("IMPORTE", totalWidth)}");
            AddLine(new string('-', lineWidth));

            foreach (var det in (orden.Detalles ?? new List<DetalleOrden>()))
            {
                var nombreServicio = det.Servicio?.NombreServicio ?? "Servicio";
                var qty = det.Cantidad.ToString(CultureInfo.InvariantCulture);
                var total = det.Total.ToString("0.00", CultureInfo.InvariantCulture);

                var nameLines = Wrap(nombreServicio, nameWidth).ToList();
                if (nameLines.Count == 0) nameLines.Add("");

                for (int i = 0; i < nameLines.Count; i++)
                {
                    if (i < nameLines.Count - 1)
                    {
                        AddLine(PadRight(nameLines[i], lineWidth));
                        continue;
                    }

                    AddLine($"{PadRight(nameLines[i], nameWidth)}{PadLeft(qty, qtyWidth)}{PadLeft(total, totalWidth)}");
                }

                if (det.Descuento > 0)
                {
                    var desc = det.Descuento.ToString("0.00", CultureInfo.InvariantCulture);
                    AddLine(PadRight($"(Desc: -{desc})", lineWidth));
                }
            }

            AddLine(new string('-', lineWidth));

            Add(0x1B, 0x45, 0x01);
            AddLine(TwoColumns("TOTAL:", $"S/. {orden.MontoTotal.ToString("0.00", CultureInfo.InvariantCulture)}", lineWidth));
            Add(0x1B, 0x45, 0x00);
            AddLine(TwoColumns("A CUENTA:", $"S/. {orden.MontoPagado.ToString("0.00", CultureInfo.InvariantCulture)}", lineWidth));
            AddLine(TwoColumns("RESTO:", $"S/. {orden.SaldoPendiente.ToString("0.00", CultureInfo.InvariantCulture)}", lineWidth));

            if (!string.IsNullOrWhiteSpace(orden.Observaciones))
            {
                AddLine(new string('-', lineWidth));
                foreach (var l in Wrap($"NOTA: {orden.Observaciones}", lineWidth))
                {
                    AddLine(l);
                }
            }

            AddLine(new string('-', lineWidth));
            Add(0x1B, 0x61, 0x01);
            AddLine("*** GRACIAS POR SU PREFERENCIA ***");
            AddLine("Revise sus prendas antes de retirar.");
            AddLine("No hay lugar a reclamo pasadas las 24hrs.");
            AddLine(orden.OrdenID.ToString(CultureInfo.InvariantCulture));

            Add(0x0A, 0x0A, 0x0A);
            Add(0x1D, 0x56, 0x42, 0x00);
        }

        private static string TwoColumns(string left, string right, int width)
        {
            left ??= "";
            right ??= "";

            if (left.Length + right.Length + 1 > width)
            {
                var maxLeft = Math.Max(0, width - right.Length - 1);
                left = left.Length > maxLeft ? left[..maxLeft] : left;
            }

            var spaces = Math.Max(1, width - left.Length - right.Length);
            return left + new string(' ', spaces) + right;
        }

        private static IEnumerable<string> Wrap(string text, int width)
        {
            text ??= "";
            text = text.Trim();
            if (text.Length == 0)
            {
                yield break;
            }

            var idx = 0;
            while (idx < text.Length)
            {
                var remaining = text.Length - idx;
                var take = Math.Min(width, remaining);
                var slice = text.Substring(idx, take);

                if (take == width && idx + take < text.Length)
                {
                    var lastSpace = slice.LastIndexOf(' ');
                    if (lastSpace >= Math.Max(1, width / 2))
                    {
                        slice = slice[..lastSpace];
                        take = slice.Length;
                    }
                }

                yield return slice.TrimEnd();
                idx += Math.Max(1, take);
                while (idx < text.Length && text[idx] == ' ')
                {
                    idx++;
                }
            }
        }

        private static string PadRight(string s, int width)
        {
            s ??= "";
            if (s.Length >= width) return s[..width];
            return s + new string(' ', width - s.Length);
        }

        private static string PadLeft(string s, int width)
        {
            s ??= "";
            if (s.Length >= width) return s[^width..];
            return new string(' ', width - s.Length) + s;
        }
    }
}
