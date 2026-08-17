using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NinjaTrader.NinjaScript.Indicators
{
    public class GammaStrikeModel
    {
        public double Strike { get; set; }
        public double CallGamma { get; set; }
        public int CallOpenInterest { get; set; }
        public double PutGamma { get; set; }
        public int PutOpenInterest { get; set; }
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
                    Regex csvSplit = new Regex(",(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)");

                    while ((line = sr.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        
                        if (line.StartsWith("UNDERLYING")) 
                        { 
                            inUnderlyingSection = true; 
                            continue; 
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
                                double price;
                                if (double.TryParse(columns[0], NumberStyles.Any, CultureInfo.InvariantCulture, out price))
                                {
                                    result.UnderlyingPrice = price;
                                    inUnderlyingSection = false;
                                }
                            }
                        }

                        if (columns.Length >= 25 && columns[14] == "Strike" && columns[5] == "Gamma")
                        {
                            inDataSection = true;
                            continue;
                        }

                        if (inDataSection)
                        {
                            if (columns.Length < 25) break;

                            double strike;
                            if (double.TryParse(columns[14], NumberStyles.Any, CultureInfo.InvariantCulture, out strike))
                            {
                                var model = new GammaStrikeModel { Strike = strike };

                                double callGamma;
                                if (double.TryParse(columns[5], NumberStyles.Any, CultureInfo.InvariantCulture, out callGamma))
                                    model.CallGamma = callGamma;

                                int callOI;
                                if (int.TryParse(columns[7].Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out callOI))
                                    model.CallOpenInterest = callOI;

                                double putGamma;
                                if (double.TryParse(columns[22], NumberStyles.Any, CultureInfo.InvariantCulture, out putGamma))
                                    model.PutGamma = putGamma;

                                int putOI;
                                if (int.TryParse(columns[24].Replace(",", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out putOI))
                                    model.PutOpenInterest = putOI;

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

            return result;
        }
    }
}
