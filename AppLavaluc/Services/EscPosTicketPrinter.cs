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
                _copies = 1;
            }

            var selected = _configuredPrinterName;
            if (string.IsNullOrWhiteSpace(selected) && RawPrinterHelper.TryGetDefaultPrinterName(out var defaultPrinter))
            {
                selected = (defaultPrinter ?? "").Trim();
            }

            if (string.IsNullOrWhiteSpace(selected))
            {
                selected = "XP-80";
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

            var comp = orden.Comprobantes?.OrderByDescending(c => c.ComprobanteID).FirstOrDefault(c => c.EstadoSunat == "Aceptado")
                     ?? orden.Comprobantes?.OrderByDescending(c => c.ComprobanteID).FirstOrDefault();
            var esFactura = (comp != null && comp.TipoDoc == "01") || (orden.Cliente?.Dni?.Length == 11);

            Add(0x1B, 0x61, 0x01);
            Add(0x1B, 0x45, 0x01);
            AddLine(copyLabel);
            AddLine("APP LAVALUC");
            Add(0x1B, 0x45, 0x00);
            AddLine(comp?.RazonSocialEmisor ?? "MONDRAGON DELGADO MAYCOL");
            AddLine($"RUC: {comp?.RucEmisor ?? "10708464100"}");
            AddLine("Av. Villa Nueva 221 - Lima");
            AddLine("Tel: (+51) 913-474-275");

            AddLine(new string('-', lineWidth));

            Add(0x1B, 0x61, 0x00);
            var ordenNum = orden.OrdenID.ToString("D6", CultureInfo.InvariantCulture);
            var fecha = (comp?.FechaEmision ?? orden.FechaRecepcion).ToString("dd/MM/yy HH:mm", CultureInfo.InvariantCulture);

            if (comp != null)
            {
                var tipoDesc = comp.TipoDoc == "01" ? "FACTURA ELECTRÓNICA" : (comp.TipoDoc == "03" ? "BOLETA ELECTRÓNICA" : "NOTA DE CRÉDITO");
                Add(0x1B, 0x45, 0x01);
                AddLine(TwoColumns(tipoDesc, comp.NumeroCompleto, lineWidth));
                Add(0x1B, 0x45, 0x00);
                AddLine(TwoColumns($"ORDEN INTERNA: #{ordenNum}", fecha, lineWidth));
            }
            else
            {
                var docDesc = esFactura ? "FACTURA (ORDEN)" : "BOLETA / TICKET";
                Add(0x1B, 0x45, 0x01);
                AddLine(TwoColumns(docDesc, $"#{ordenNum}", lineWidth));
                Add(0x1B, 0x45, 0x00);
                AddLine($"Fecha: {fecha}");
            }

            AddLine(new string('-', lineWidth));

            // Datos del Cliente Receptor
            if (esFactura)
            {
                var rucCli = comp?.NumDocCliente ?? orden.Cliente?.Dni ?? "--";
                var rznCli = comp?.RznSocialCliente ?? orden.Cliente?.NombreCompleto ?? "--";
                var dirCli = comp?.DireccionCliente ?? orden.Cliente?.Direccion ?? "--";

                AddLine($"RUC RECEPTOR: {rucCli}");
                Add(0x1B, 0x45, 0x01);
                foreach (var l in Wrap($"RAZON SOCIAL: {rznCli}", lineWidth))
                {
                    AddLine(l);
                }
                Add(0x1B, 0x45, 0x00);
                foreach (var l in Wrap($"DIR. FISCAL: {dirCli}", lineWidth))
                {
                    AddLine(l);
                }
            }
            else
            {
                var dniCli = comp?.NumDocCliente ?? (string.IsNullOrWhiteSpace(orden.Cliente?.Dni) ? "SIN DOCUMENTO" : orden.Cliente.Dni);
                var nomCli = comp?.RznSocialCliente ?? orden.Cliente?.NombreCompleto ?? "CLIENTES VARIOS";

                AddLine($"DNI: {dniCli}");
                Add(0x1B, 0x45, 0x01);
                foreach (var l in Wrap($"CLIENTE: {nomCli}", lineWidth))
                {
                    AddLine(l);
                }
                Add(0x1B, 0x45, 0x00);
            }

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

            // Cálculos Tributarios SUNAT
            decimal totalGeneral = orden.MontoTotal;
            decimal opGravada = comp != null && comp.MontoOperacionesGravadas > 0
                ? comp.MontoOperacionesGravadas
                : Math.Round(totalGeneral / 1.18m, 2);
            decimal totalIgv = comp != null && comp.MontoIgv > 0
                ? comp.MontoIgv
                : (totalGeneral - opGravada);

            AddLine(TwoColumns("OP. GRAVADA:", $"S/. {opGravada.ToString("0.00", CultureInfo.InvariantCulture)}", lineWidth));
            AddLine(TwoColumns("I.G.V. (18%):", $"S/. {totalIgv.ToString("0.00", CultureInfo.InvariantCulture)}", lineWidth));

            Add(0x1B, 0x45, 0x01);
            AddLine(TwoColumns("IMPORTE TOTAL:", $"S/. {totalGeneral.ToString("0.00", CultureInfo.InvariantCulture)}", lineWidth));
            Add(0x1B, 0x45, 0x00);

            try
            {
                var letras = AppLavaluc.Helpers.NumeroLetrasHelper.Convertir(totalGeneral);
                foreach (var l in Wrap($"SON: {letras}", lineWidth))
                {
                    AddLine(l);
                }
            }
            catch { }

            AddLine(new string('-', lineWidth));
            AddLine(TwoColumns("A CUENTA / PAGADO:", $"S/. {orden.MontoPagado.ToString("0.00", CultureInfo.InvariantCulture)}", lineWidth));
            AddLine(TwoColumns("SALDO PENDIENTE:", $"S/. {orden.SaldoPendiente.ToString("0.00", CultureInfo.InvariantCulture)}", lineWidth));

            if (!string.IsNullOrWhiteSpace(orden.Observaciones))
            {
                AddLine(new string('-', lineWidth));
                foreach (var l in Wrap($"NOTA: {orden.Observaciones}", lineWidth))
                {
                    AddLine(l);
                }
            }

            if (!string.IsNullOrWhiteSpace(comp?.HashCdr))
            {
                AddLine(new string('-', lineWidth));
                AddLine($"Hash CDR: {comp.HashCdr}");
            }

            AddLine(new string('-', lineWidth));
            Add(0x1B, 0x61, 0x01);
            if (comp != null)
            {
                AddLine($"Representacion impresa de la {(esFactura ? "FACTURA" : "BOLETA")} ELECTRONICA");
                AddLine("Consulte su validez en www.sunat.gob.pe");
            }
            AddLine("*** GRACIAS POR SU PREFERENCIA ***");
            AddLine("Revise sus prendas antes de retirar.");
            AddLine("No hay lugar a reclamo pasadas las 24hrs.");
            AddLine($"ORDEN #{orden.OrdenID}");

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
