using System;
using System.Collections.Generic;

namespace NinjaTrader.NinjaScript.Indicators
{
    public class GammaLevelsResult
    {
        public double CallWallStrike { get; set; }
        public double PutWallStrike { get; set; }
        public double GammaFlipStrike { get; set; }
        public double MaxCallVolStrike { get; set; }
        public double MaxPutVolStrike { get; set; }
        public double MaxCallOiStrike { get; set; }
        public double MaxPutOiStrike { get; set; }
        public double TotalNetGex { get; set; }
        public bool IsValid { get; set; }
    }

    public static class GammaLevelsAnalyzer
    {
        public static GammaLevelsResult Analyze(List<GammaStrikeModel> strikes, double underlyingPrice)
        {
            var result = new GammaLevelsResult();

            if (strikes == null || strikes.Count == 0)
                return result;

            double totalNetGex = 0;

            // Dictionary to store NetGEX by Strike for Gamma Flip calculation
            var netGexByStrike = new Dictionary<double, double>();

            int maxCallVol = 0;
            int maxPutVol = 0;
            int maxCallOi = 0;
            int maxPutOi = 0;

            foreach (var strike in strikes)
            {
                // Multiply by 100 for standard options multiplier
                double callGEX = strike.CallGamma * strike.CallOpenInterest * 100;
                double putGEX = strike.PutGamma * strike.PutOpenInterest * 100 * -1; // Put GEX is negative
                double netGEX = callGEX + putGEX;

                totalNetGex += netGEX;
                
                if (!netGexByStrike.ContainsKey(strike.Strike))
                    netGexByStrike[strike.Strike] = 0;
                netGexByStrike[strike.Strike] += netGEX;

                if (strike.CallVolume > maxCallVol) { maxCallVol = strike.CallVolume; result.MaxCallVolStrike = strike.Strike; }
                if (strike.PutVolume > maxPutVol) { maxPutVol = strike.PutVolume; result.MaxPutVolStrike = strike.Strike; }
                if (strike.CallOpenInterest > maxCallOi) { maxCallOi = strike.CallOpenInterest; result.MaxCallOiStrike = strike.Strike; }
                if (strike.PutOpenInterest > maxPutOi) { maxPutOi = strike.PutOpenInterest; result.MaxPutOiStrike = strike.Strike; }
            }

            // Call Wall: Strike con el Net GEX más POSITIVO, por encima o igual al precio del subyacente
            // Put Wall: Strike con el Net GEX más NEGATIVO, por debajo o igual al precio del subyacente
            double maxPositiveGex = 0;
            double maxNegativeGex = 0;

            foreach (var kvp in netGexByStrike)
            {
                double strikePrice = kvp.Key;
                double netGex = kvp.Value;

                if (strikePrice >= underlyingPrice && netGex > maxPositiveGex)
                {
                    maxPositiveGex = netGex;
                    result.CallWallStrike = strikePrice;
                }

                if (strikePrice <= underlyingPrice && netGex < maxNegativeGex)
                {
                    maxNegativeGex = netGex;
                    result.PutWallStrike = strikePrice;
                }
            }

            // Fallback: si no encontró Call Wall por encima, buscar el mayor positivo en cualquier lugar
            if (result.CallWallStrike == 0)
            {
                foreach (var kvp in netGexByStrike)
                {
                    if (kvp.Value > maxPositiveGex) { maxPositiveGex = kvp.Value; result.CallWallStrike = kvp.Key; }
                }
            }
            // Fallback: si no encontró Put Wall por debajo, buscar el más negativo en cualquier lugar
            if (result.PutWallStrike == 0)
            {
                foreach (var kvp in netGexByStrike)
                {
                    if (kvp.Value < maxNegativeGex) { maxNegativeGex = kvp.Value; result.PutWallStrike = kvp.Key; }
                }
            }

            // Gamma Flip: find where NetGEX is closest to 0 BETWEEN the walls.
            double closestToZeroDiff = double.MaxValue;
            double gammaFlipStrike = 0;
            
            double minWall = Math.Min(result.PutWallStrike, result.CallWallStrike);
            double maxWall = Math.Max(result.PutWallStrike, result.CallWallStrike);

            foreach (var kvp in netGexByStrike)
            {
                if (kvp.Key < minWall || kvp.Key > maxWall)
                    continue;

                double absGex = Math.Abs(kvp.Value);
                if (absGex < closestToZeroDiff)
                {
                    closestToZeroDiff = absGex;
                    gammaFlipStrike = kvp.Key;
                }
            }
            
            // Fallback sin restricción
            if (gammaFlipStrike == 0)
            {
                foreach (var kvp in netGexByStrike)
                {
                    if (Math.Abs(kvp.Value) < closestToZeroDiff)
                    {
                        closestToZeroDiff = Math.Abs(kvp.Value);
                        gammaFlipStrike = kvp.Key;
                    }
                }
            }

            result.TotalNetGex = totalNetGex;
            result.GammaFlipStrike = gammaFlipStrike;
            result.IsValid = true;

            return result;
        }
    }
}
