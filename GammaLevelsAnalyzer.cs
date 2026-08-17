using System;
using System.Collections.Generic;

namespace NinjaTrader.NinjaScript.Indicators
{
    public class GammaLevelsResult
    {
        public double CallWallStrike { get; set; }
        public double PutWallStrike { get; set; }
        public double GammaFlipStrike { get; set; }
        public double TotalNetGex { get; set; }
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
            double totalNetGex = 0;

            // Dictionary to store NetGEX by Strike for Gamma Flip calculation
            var netGexByStrike = new Dictionary<double, double>();

            foreach (var strike in strikes)
            {
                // Multiply by 100 for standard options multiplier
                double callGEX = strike.CallGamma * strike.CallOpenInterest * 100;
                double putGEX = strike.PutGamma * strike.PutOpenInterest * 100 * -1; // Put GEX is negative
                double netGEX = callGEX + putGEX;

                totalNetGex += netGEX;
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

            // Gamma Flip: find where NetGEX is closest to 0.
            // Para evitar que tome strikes muy profundos OTM (donde el GEX es casi 0 por falta de liquidez),
            // restringimos la búsqueda del Zero Gravity / Flip para que esté estrictamente ENTRE el Put Wall y el Call Wall.
            double closestToZeroDiff = double.MaxValue;
            double gammaFlipStrike = 0;
            
            double minWall = Math.Min(result.PutWallStrike, result.CallWallStrike);
            double maxWall = Math.Max(result.PutWallStrike, result.CallWallStrike);

            foreach (var strike in strikes)
            {
                if (strike.CallGamma == 0 && strike.PutGamma == 0)
                    continue;

                // Buscar solo entre las paredes principales
                if (strike.Strike < minWall || strike.Strike > maxWall)
                    continue;

                double callGEX = strike.CallGamma * strike.CallOpenInterest * 100;
                double putGEX = strike.PutGamma * strike.PutOpenInterest * 100 * -1;
                double netGEX = callGEX + putGEX;
                
                double absGex = Math.Abs(netGEX);
                if (absGex < closestToZeroDiff)
                {
                    closestToZeroDiff = absGex;
                    gammaFlipStrike = strike.Strike;
                }
            }
            
            // Si por alguna razón extrema no encontró nada, hacemos un fallback sin restricción
            if (gammaFlipStrike == 0)
            {
                foreach (var strike in strikes)
                {
                    if (strike.CallGamma == 0 && strike.PutGamma == 0) continue;
                    double netGEX = (strike.CallGamma * strike.CallOpenInterest * 100) + (strike.PutGamma * strike.PutOpenInterest * 100 * -1);
                    if (Math.Abs(netGEX) < closestToZeroDiff)
                    {
                        closestToZeroDiff = Math.Abs(netGEX);
                        gammaFlipStrike = strike.Strike;
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
