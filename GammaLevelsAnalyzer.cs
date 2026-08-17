using System;
using System.Collections.Generic;

namespace NinjaTrader.NinjaScript.Indicators
{
    public class GammaLevelsResult
    {
        public double CallWallStrike { get; set; }
        public double PutWallStrike { get; set; }
        public double GammaFlipStrike { get; set; }
        public bool IsValid { get; set; }
    }

    public static class GammaLevelsAnalyzer
    {
        public static GammaLevelsResult Analyze(List<GammaStrikeModel> strikes)
        {
            var result = new GammaLevelsResult();

            if (strikes == null || strikes.Count == 0)
                return result;

            double maxCallGEX = double.MinValue;
            double minPutGEX = double.MaxValue; // Look for the most negative GEX

            // Dictionary to store NetGEX by Strike for Gamma Flip calculation
            var netGexByStrike = new Dictionary<double, double>();

            foreach (var strike in strikes)
            {
                // Multiply by 100 for standard options multiplier
                double callGEX = strike.CallGamma * strike.CallOpenInterest * 100;
                double putGEX = strike.PutGamma * strike.PutOpenInterest * 100 * -1; // Put GEX is negative
                double netGEX = callGEX + putGEX;

                netGexByStrike[strike.Strike] = netGEX;

                if (callGEX > maxCallGEX)
                {
                    maxCallGEX = callGEX;
                    result.CallWallStrike = strike.Strike;
                }

                if (putGEX < minPutGEX) // Note: less than because we want the most negative value
                {
                    minPutGEX = putGEX;
                    result.PutWallStrike = strike.Strike;
                }
            }

            // Gamma Flip: find where NetGEX crosses 0.
            // A simple approximation is the strike with the NetGEX closest to 0.
            double closestToZeroDiff = double.MaxValue;
            double gammaFlipStrike = 0;
            
            foreach (var kvp in netGexByStrike)
            {
                double absGex = Math.Abs(kvp.Value);
                if (absGex < closestToZeroDiff)
                {
                    closestToZeroDiff = absGex;
                    gammaFlipStrike = kvp.Key;
                }
            }

            result.GammaFlipStrike = gammaFlipStrike;
            result.IsValid = true;

            return result;
        }
    }
}
