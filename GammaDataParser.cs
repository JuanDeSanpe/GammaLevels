using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Linq;

namespace NinjaTrader.NinjaScript.Indicators
{
    public class GammaStrikeModel
    {
        public double Strike { get; set; }
        public DateTime ExpirationDate { get; set; }
        public double CallGamma { get; set; }
        public int CallOpenInterest { get; set; }
        public int CallVolume { get; set; }
        public double PutGamma { get; set; }
        public int PutOpenInterest { get; set; }
        public int PutVolume { get; set; }
    }

    public class GammaParseResult
    {
        public List<GammaStrikeModel> Strikes { get; set; }
        public double UnderlyingPrice { get; set; }

        public GammaParseResult()
        {
            Strikes = new List<GammaStrikeModel>();
        }
    }

    public static class GammaDataParser
    {
        private static double ParseDouble(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            s = s.Trim();
            
            // Si tiene coma y punto (ej: 1,000.50), quitamos la coma de los miles
            if (s.Contains(",") && s.Contains("."))
            {
                s = s.Replace(",", "");
            }
            // Si solo tiene coma (ej: 0,05 español), la cambiamos a punto decimal
            else if (s.Contains(","))
            {
                s = s.Replace(",", ".");
            }
            
            double result;
            double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
            return result;
        }

        public static GammaParseResult ParseCSV(string filePath, Action<string> logError)
        {
            var result = new GammaParseResult();
            if (!File.Exists(filePath)) return result;

            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    string line;
                    bool inDataSection = false;
                    bool inUnderlyingSection = false;
                    Regex csvSplit = null;
                    int colStrike = -1, colExp = -1, colCallGamma = -1, colCallOI = -1, colCallVol = -1, colPutGamma = -1, colPutOI = -1, colPutVol = -1;

                    while ((line = sr.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        
                        if (line.StartsWith("UNDERLYING;") || line.StartsWith("UNDERLYING,")) 
                        { 
                            inUnderlyingSection = true; 
                            continue; 
                        }

                        if (csvSplit == null)
                        {
                            if (line.Contains(";"))
                                csvSplit = new Regex(";(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)");
                            else
                                csvSplit = new Regex(",(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)");
                        }

                        var columns = csvSplit.Split(line);
                        for (int i = 0; i < columns.Length; i++)
                        {
                            columns[i] = columns[i].Trim('\"').Trim();
                        }

                        if (inUnderlyingSection && columns.Length >= 12)
                        {
                            if (columns[0] != "LAST") // Saltamos la cabecera
                            {
                                double price = ParseDouble(columns[0]);
                                if (price > 0)
                                {
                                    // Si Excel en español quitó el punto decimal (ej. 731.07 -> 73107)
                                    if (price > 10000) price = price / 100.0;
                                    
                                    result.UnderlyingPrice = price;
                                    inUnderlyingSection = false;
                                }
                            }
                        }

                        // Detect Headers
                        if (!inDataSection && columns.Length > 5 && columns.Contains("Strike") && columns.Contains("Gamma"))
                        {
                            colStrike = Array.IndexOf(columns, "Strike");
                            colExp = Array.IndexOf(columns, "Exp");
                            colCallGamma = Array.IndexOf(columns, "Gamma");
                            colCallOI = Array.IndexOf(columns, "Open.Int");
                            colCallVol = Array.IndexOf(columns, "Volume");
                            
                            colPutGamma = Array.LastIndexOf(columns, "Gamma");
                            colPutOI = Array.LastIndexOf(columns, "Open.Int");
                            colPutVol = Array.LastIndexOf(columns, "Volume");

                            if (colStrike != -1 && colCallGamma != -1 && colPutGamma != -1 && colCallGamma != colPutGamma)
                            {
                                inDataSection = true;
                            }
                            continue;
                        }

                        if (inDataSection)
                        {
                            if (columns.Length <= Math.Max(colPutGamma, colPutOI)) continue;

                            double strike = ParseDouble(columns[colStrike]);
                            if (strike > 0)
                            {
                                DateTime expDate = DateTime.MinValue;
                                if (colExp != -1 && colExp < columns.Length && !string.IsNullOrWhiteSpace(columns[colExp]))
                                {
                                    DateTime.TryParseExact(columns[colExp], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out expDate);
                                }
                                
                                var model = new GammaStrikeModel { Strike = strike, ExpirationDate = expDate };

                                model.CallGamma = ParseDouble(columns[colCallGamma]);

                                int callOI;
                                if (int.TryParse(columns[colCallOI].Replace(",", "").Replace(".", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out callOI))
                                    model.CallOpenInterest = callOI;

                                int callVol;
                                if (colCallVol != -1 && colCallVol < columns.Length && int.TryParse(columns[colCallVol].Replace(",", "").Replace(".", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out callVol))
                                    model.CallVolume = callVol;

                                model.PutGamma = ParseDouble(columns[colPutGamma]);

                                int putOI;
                                if (int.TryParse(columns[colPutOI].Replace(",", "").Replace(".", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out putOI))
                                    model.PutOpenInterest = putOI;

                                int putVol;
                                if (colPutVol != -1 && colPutVol < columns.Length && int.TryParse(columns[colPutVol].Replace(",", "").Replace(".", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out putVol))
                                    model.PutVolume = putVol;

                                // Fallback: si no tenemos UnderlyingPrice, usamos el Strike ATM (donde Gamma es máxima)
                                if (result.UnderlyingPrice == 0 && (model.CallGamma > 0.05 || model.PutGamma > 0.05))
                                {
                                    result.UnderlyingPrice = strike; // Aproximación muy cercana
                                }

                                result.Strikes.Add(model);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (logError != null) logError("GammaDataParser Error: " + ex.Message);
            }

            // Auto-corrección de magnitud: El precio del subyacente DEBE tener la misma magnitud que los strikes.
            // Si por culpa de los decimales de Excel el subyacente es 7316.89 y los strikes son 730, lo dividimos entre 10.
            if (result.UnderlyingPrice > 0 && result.Strikes.Count > 0)
            {
                // Cogemos un strike cualquiera (el del medio de la cadena suele estar cerca del precio)
                double sampleStrike = result.Strikes[result.Strikes.Count / 2].Strike;
                
                if (sampleStrike > 0)
                {
                    while (result.UnderlyingPrice > sampleStrike * 3)
                    {
                        result.UnderlyingPrice /= 10.0;
                    }
                    while (result.UnderlyingPrice < sampleStrike / 3)
                    {
                        result.UnderlyingPrice *= 10.0;
                    }
                }
            }

            return result;
        }
    }
}
