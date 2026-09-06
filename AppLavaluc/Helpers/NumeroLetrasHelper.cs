using System;

namespace AppLavaluc.Helpers
{
    public static class NumeroLetrasHelper
    {
        private static readonly string[] Unidades = 
        { 
            "", "UN", "DOS", "TRES", "CUATRO", "CINCO", "SEIS", "SIETE", "OCHO", "NUEVE", 
            "DIEZ", "ONCE", "DOCE", "TRECE", "CATORCE", "QUINCE", "DIECISÉIS", "DIECISIETE", 
            "DIECIOCHO", "DIECINUEVE", "VEINTE" 
        };

        private static readonly string[] Decenas = 
        { 
            "", "DIEZ", "VEINTE", "TREINTA", "CUARENTA", "CINCUENTA", 
            "SESENTA", "SETENTA", "OCHENTA", "NOVENTA" 
        };

        private static readonly string[] Centenas = 
        { 
            "", "CIENTO", "DOSCIENTOS", "TRESCIENTOS", "CUATROCIENTOS", 
            "QUINIENTOS", "SEISCIENTOS", "SETECIENTOS", "OCHOCIENTOS", "NOVECIENTOS" 
        };

        public static string Convertir(decimal numero, string moneda = "SOLES")
        {
            if (numero < 0) numero = Math.Abs(numero);
            long entero = (long)Math.Truncate(numero);
            int decimales = (int)Math.Round((numero - entero) * 100, 0);

            string letras = ConvertirEntero(entero);
            return $"{letras} CON {decimales:D2}/100 {moneda}".Trim();
        }

        private static string ConvertirEntero(long n)
        {
            if (n == 0) return "CERO";
            if (n == 100) return "CIEN";

            if (n < 0) return "MENOS " + ConvertirEntero(-n);

            if (n < 1000)
            {
                string s = "";
                if (n >= 100)
                {
                    long c = n / 100;
                    n %= 100;
                    s += Centenas[c] + " ";
                }

                if (n > 0)
                {
                    if (n <= 20)
                    {
                        s += Unidades[n];
                    }
                    else if (n < 30)
                    {
                        s += "VEINTI" + Unidades[n - 20];
                    }
                    else
                    {
                        long d = n / 10;
                        long u = n % 10;
                        s += Decenas[d];
                        if (u > 0) s += " Y " + Unidades[u];
                    }
                }
                return s.Trim();
            }

            if (n < 1000000)
            {
                long miles = n / 1000;
                long resto = n % 1000;
                string sMiles = miles == 1 ? "UN MIL" : ConvertirEntero(miles) + " MIL";
                if (resto > 0) sMiles += " " + ConvertirEntero(resto);
                return sMiles.Trim();
            }

            if (n < 1000000000000)
            {
                long millones = n / 1000000;
                long resto = n % 1000000;
                string sMillones = millones == 1 ? "UN MILLÓN" : ConvertirEntero(millones) + " MILLONES";
                if (resto > 0) sMillones += " " + ConvertirEntero(resto);
                return sMillones.Trim();
            }

            return n.ToString();
        }
    }
}
