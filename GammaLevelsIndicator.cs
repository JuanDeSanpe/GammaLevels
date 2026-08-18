using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using System.IO;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Linq;
using NinjaTrader.NinjaScript.Indicators;

namespace NinjaTrader.NinjaScript.Indicators
{
    public enum GammaDisplayMode
    {
        Both,
        Only0DTE,
        OnlyMacro
    }

    public class GammaLevelsIndicator : Indicator
    {
        private System.Threading.Timer refreshTimer;
        private string folderPath;
        private double lastKnownNqPrice;
        private DateTime lastKnownTime;
        private DateTime sessionStartTime;

        private double lastCall0DTE = 0;
        private double lastPut0DTE = 0;
        private double lastFlip0DTE = 0;
        private double lastMaxCallVol0DTE = 0;
        private double lastMaxPutVol0DTE = 0;
        private double lastMaxCallOi0DTE = 0;
        private double lastMaxPutOi0DTE = 0;
        
        private double lastCallMacro = 0;
        private double lastPutMacro = 0;
        private double lastFlipMacro = 0;
        private double lastMaxCallVolMacro = 0;
        private double lastMaxPutVolMacro = 0;
        private double lastMaxCallOiMacro = 0;
        private double lastMaxPutOiMacro = 0;
        private double savedRatio = 0;
        private string label0DTE = "";
        private string currentHudText = "";
        private bool needsRedraw = false;
        private bool isRealTime = false;
        private int barCount = 0;

        [NinjaScriptProperty]
        [Display(Name="File Name", Description="Name of the CSV file in Archivos Cadena de Opciones folder", Order=1, GroupName="Parameters")]
        public string FileName { get; set; }

        [NinjaScriptProperty]
        [Range(1, 60)]
        [Display(Name="Refresh Interval (Seconds)", Description="How often to read the file", Order=2, GroupName="Parameters")]
        public int RefreshInterval { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Display Mode", Description="Mostrar 0DTE, Macro o ambos", Order=3, GroupName="Parameters")]
        public GammaDisplayMode DisplayMode { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Displays Gamma Levels from TOS Options Chain CSV";
                Name = "GammaLevelsIndicator";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                FileName = "CadenaDeOpcionesQQQ.csv";
                RefreshInterval = 5;
                DisplayMode = GammaDisplayMode.Both;
            }
            else if (State == State.Configure)
            {
                folderPath = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "bin", "Custom", "Indicators", "TOS Opciones", "Archivos Cadena de Opciones");
            }
            else if (State == State.Terminated)
            {
                if (refreshTimer != null)
                {
                    refreshTimer.Dispose();
                    refreshTimer = null;
                }
            }
        }

        private void TimerCallback(object state)
        {
            if (!isRealTime || lastKnownNqPrice == 0) return;

            try
            {
                string fullPath = Path.Combine(folderPath, FileName);
                if (!File.Exists(fullPath)) return;

                DateTime fileTime = File.GetLastWriteTime(fullPath);

                var parsedData = GammaDataParser.ParseCSV(fullPath, msg => Print(msg));
                var validStrikes = parsedData.Strikes.Where(s => s.ExpirationDate > DateTime.MinValue).ToList();
                List<GammaStrikeModel> strikes0DTE = parsedData.Strikes;
                List<GammaStrikeModel> strikesMacro = parsedData.Strikes;

                if (validStrikes.Count > 0)
                {
                    DateTime minDate = validStrikes.Min(s => s.ExpirationDate);
                    strikes0DTE = validStrikes.Where(s => s.ExpirationDate == minDate).ToList();
                }

                var levels0DTE = GammaLevelsAnalyzer.Analyze(strikes0DTE, parsedData.UnderlyingPrice);
                var levelsMacro = GammaLevelsAnalyzer.Analyze(strikesMacro, parsedData.UnderlyingPrice);

                if ((levels0DTE.IsValid || levelsMacro.IsValid) && parsedData.UnderlyingPrice > 0)
                {
                    if (savedRatio == 0)
                    {
                        savedRatio = lastKnownNqPrice / parsedData.UnderlyingPrice;
                    }
                    
                    double ratioToUse = savedRatio;

                    lastCall0DTE = levels0DTE.CallWallStrike * ratioToUse;
                    lastPut0DTE = levels0DTE.PutWallStrike * ratioToUse;
                    lastFlip0DTE = levels0DTE.GammaFlipStrike * ratioToUse;
                    lastMaxCallVol0DTE = levels0DTE.MaxCallVolStrike * ratioToUse;
                    lastMaxPutVol0DTE = levels0DTE.MaxPutVolStrike * ratioToUse;
                    lastMaxCallOi0DTE = levels0DTE.MaxCallOiStrike * ratioToUse;
                    lastMaxPutOi0DTE = levels0DTE.MaxPutOiStrike * ratioToUse;

                    lastCallMacro = levelsMacro.CallWallStrike * ratioToUse;
                    lastPutMacro = levelsMacro.PutWallStrike * ratioToUse;
                    lastFlipMacro = levelsMacro.GammaFlipStrike * ratioToUse;
                    lastMaxCallVolMacro = levelsMacro.MaxCallVolStrike * ratioToUse;
                    lastMaxPutVolMacro = levelsMacro.MaxPutVolStrike * ratioToUse;
                    lastMaxCallOiMacro = levelsMacro.MaxCallOiStrike * ratioToUse;
                    lastMaxPutOiMacro = levelsMacro.MaxPutOiStrike * ratioToUse;
                    label0DTE = validStrikes.Count > 0 ? validStrikes.Min(s => s.ExpirationDate).ToString("dd/MMM") : "0DTE";
                    
                    currentHudText = "0DTE: " + (levels0DTE.TotalNetGex >= 0 ? "POS (Baja Vol)" : "NEG (Alta Vol)") + "\nMACRO: " + (levelsMacro.TotalNetGex >= 0 ? "POS (Baja Vol)" : "NEG (Alta Vol)");
                    needsRedraw = true;
                }
            }
            catch (Exception ex)
            {
                Print("GammaLevelsIndicator Timer Error: " + ex.Message);
            }
        }

        protected override void OnBarUpdate()
        {
            // ====== HISTORICO: cero trabajo, solo mostrar progreso ======
            if (State == State.Historical)
            {
                barCount++;
                if (barCount % 10000 == 0)
                {
                    Draw.TextFixed(this, "GammaHUD", "Gamma Levels: Cargando grafico... " + barCount + " barras", TextPosition.TopRight, Brushes.Gray, new Gui.Tools.SimpleFont("Arial", 11) { Bold = true }, Brushes.Transparent, Brushes.Transparent, 0);
                }
                return;
            }

            // ====== TIEMPO REAL ======
            if (!isRealTime)
            {
                isRealTime = true;
                lastKnownNqPrice = Close[0];
                lastKnownTime = Time[0];
                sessionStartTime = Time[0].Date;
                
                // Arrancar timer AHORA, no antes
                if (refreshTimer == null)
                    refreshTimer = new System.Threading.Timer(TimerCallback, null, 0, RefreshInterval * 1000);
                
                Draw.TextFixed(this, "GammaHUD", "Gamma Levels: Leyendo CSV...", TextPosition.TopRight, Brushes.Yellow, new Gui.Tools.SimpleFont("Arial", 11) { Bold = true }, Brushes.Transparent, Brushes.Transparent, 0);
                return;
            }

            lastKnownNqPrice = Close[0];
            lastKnownTime = Time[0];

            if (Bars.IsFirstBarOfSession)
                sessionStartTime = Time[0];

            if (!needsRedraw) return;

            // HUD
            if (!string.IsNullOrEmpty(currentHudText))
                Draw.TextFixed(this, "GammaHUD", currentHudText, TextPosition.TopRight, Brushes.LightGray, new Gui.Tools.SimpleFont("Arial", 11) { Bold = true }, Brushes.Transparent, Brushes.Transparent, 0);

            DateTime futureTime = Time[0].AddDays(5);

            // 0DTE
            if (DisplayMode == GammaDisplayMode.Both || DisplayMode == GammaDisplayMode.Only0DTE)
            {
                if (lastCall0DTE > 0)
                {
                    Draw.Line(this, "CallWall_0DTE", false, sessionStartTime, lastCall0DTE, futureTime, lastCall0DTE, Brushes.LimeGreen, DashStyleHelper.Solid, 2);
                    var t1 = Draw.Text(this, "Txt_Call_0DTE", "Call 0DTE (" + label0DTE + ")", 5, lastCall0DTE + 8, Brushes.LimeGreen);
                    if (t1 != null) t1.Font = new Gui.Tools.SimpleFont("Arial", 9);
                }
                if (lastPut0DTE > 0)
                {
                    Draw.Line(this, "PutWall_0DTE", false, sessionStartTime, lastPut0DTE, futureTime, lastPut0DTE, Brushes.Red, DashStyleHelper.Solid, 2);
                    var t2 = Draw.Text(this, "Txt_Put_0DTE", "Put 0DTE (" + label0DTE + ")", 5, lastPut0DTE - 8, Brushes.Red);
                    if (t2 != null) t2.Font = new Gui.Tools.SimpleFont("Arial", 9);
                }
                if (lastFlip0DTE > 0)
                {
                    Draw.Line(this, "Flip_0DTE", false, sessionStartTime, lastFlip0DTE, futureTime, lastFlip0DTE, Brushes.Yellow, DashStyleHelper.Dash, 2);
                    var t3 = Draw.Text(this, "Txt_Flip_0DTE", "Flip 0DTE (" + label0DTE + ")", 5, lastFlip0DTE + 8, Brushes.Yellow);
                    if (t3 != null) t3.Font = new Gui.Tools.SimpleFont("Arial", 9);
                }
                
                if (lastMaxCallVol0DTE > 0)
                {
                    Draw.Line(this, "MaxCallVol_0DTE", false, sessionStartTime, lastMaxCallVol0DTE, futureTime, lastMaxCallVol0DTE, Brushes.Cyan, DashStyleHelper.Dot, 2);
                    var t = Draw.Text(this, "Txt_MaxCallVol_0DTE", "Max Call Vol (" + label0DTE + ")", 5, lastMaxCallVol0DTE + 8, Brushes.Cyan);
                    if (t != null) t.Font = new Gui.Tools.SimpleFont("Arial", 9);
                }
                if (lastMaxPutVol0DTE > 0)
                {
                    Draw.Line(this, "MaxPutVol_0DTE", false, sessionStartTime, lastMaxPutVol0DTE, futureTime, lastMaxPutVol0DTE, Brushes.Magenta, DashStyleHelper.Dot, 2);
                    var t = Draw.Text(this, "Txt_MaxPutVol_0DTE", "Max Put Vol (" + label0DTE + ")", 5, lastMaxPutVol0DTE - 8, Brushes.Magenta);
                    if (t != null) t.Font = new Gui.Tools.SimpleFont("Arial", 9);
                }
                if (lastMaxCallOi0DTE > 0)
                {
                    Draw.Line(this, "MaxCallOi_0DTE", false, sessionStartTime, lastMaxCallOi0DTE, futureTime, lastMaxCallOi0DTE, Brushes.DarkOliveGreen, DashStyleHelper.DashDot, 2);
                    var t = Draw.Text(this, "Txt_MaxCallOi_0DTE", "Max Call OI (" + label0DTE + ")", 5, lastMaxCallOi0DTE + 8, Brushes.DarkOliveGreen);
                    if (t != null) t.Font = new Gui.Tools.SimpleFont("Arial", 9);
                }
                if (lastMaxPutOi0DTE > 0)
                {
                    Draw.Line(this, "MaxPutOi_0DTE", false, sessionStartTime, lastMaxPutOi0DTE, futureTime, lastMaxPutOi0DTE, Brushes.Maroon, DashStyleHelper.DashDot, 2);
                    var t = Draw.Text(this, "Txt_MaxPutOi_0DTE", "Max Put OI (" + label0DTE + ")", 5, lastMaxPutOi0DTE - 8, Brushes.Maroon);
                    if (t != null) t.Font = new Gui.Tools.SimpleFont("Arial", 9);
                }
            }

            // Macro
            if (DisplayMode == GammaDisplayMode.Both || DisplayMode == GammaDisplayMode.OnlyMacro)
            {
                if (lastCallMacro > 0)
                {
                    Draw.Line(this, "CallWall_Macro", false, sessionStartTime, lastCallMacro, futureTime, lastCallMacro, Brushes.DarkGreen, DashStyleHelper.Solid, 3);
                    var t4 = Draw.Text(this, "Txt_Call_Macro", "Call MACRO", 5, lastCallMacro + 20, Brushes.DarkGreen);
                    if (t4 != null) t4.Font = new Gui.Tools.SimpleFont("Arial", 9);
                }
                if (lastPutMacro > 0)
                {
                    Draw.Line(this, "PutWall_Macro", false, sessionStartTime, lastPutMacro, futureTime, lastPutMacro, Brushes.DarkRed, DashStyleHelper.Solid, 3);
                    var t5 = Draw.Text(this, "Txt_Put_Macro", "Put MACRO", 5, lastPutMacro - 20, Brushes.DarkRed);
                    if (t5 != null) t5.Font = new Gui.Tools.SimpleFont("Arial", 9);
                }
                if (lastFlipMacro > 0)
                {
                    Draw.Line(this, "Flip_Macro", false, sessionStartTime, lastFlipMacro, futureTime, lastFlipMacro, Brushes.Goldenrod, DashStyleHelper.Dash, 3);
                    var t6 = Draw.Text(this, "Txt_Flip_Macro", "Flip MACRO", 5, lastFlipMacro + 20, Brushes.Goldenrod);
                    if (t6 != null) t6.Font = new Gui.Tools.SimpleFont("Arial", 9);
                }
                
                if (lastMaxCallVolMacro > 0)
                {
                    Draw.Line(this, "MaxCallVol_Macro", false, sessionStartTime, lastMaxCallVolMacro, futureTime, lastMaxCallVolMacro, Brushes.DarkCyan, DashStyleHelper.Dot, 3);
                    var t = Draw.Text(this, "Txt_MaxCallVol_Macro", "Max Call Vol MACRO", 5, lastMaxCallVolMacro + 20, Brushes.DarkCyan);
                    if (t != null) t.Font = new Gui.Tools.SimpleFont("Arial", 9);
                }
                if (lastMaxPutVolMacro > 0)
                {
                    Draw.Line(this, "MaxPutVol_Macro", false, sessionStartTime, lastMaxPutVolMacro, futureTime, lastMaxPutVolMacro, Brushes.DarkMagenta, DashStyleHelper.Dot, 3);
                    var t = Draw.Text(this, "Txt_MaxPutVol_Macro", "Max Put Vol MACRO", 5, lastMaxPutVolMacro - 20, Brushes.DarkMagenta);
                    if (t != null) t.Font = new Gui.Tools.SimpleFont("Arial", 9);
                }
                if (lastMaxCallOiMacro > 0)
                {
                    Draw.Line(this, "MaxCallOi_Macro", false, sessionStartTime, lastMaxCallOiMacro, futureTime, lastMaxCallOiMacro, Brushes.DarkOliveGreen, DashStyleHelper.DashDot, 3);
                    var t = Draw.Text(this, "Txt_MaxCallOi_Macro", "Max Call OI MACRO", 5, lastMaxCallOiMacro + 20, Brushes.DarkOliveGreen);
                    if (t != null) t.Font = new Gui.Tools.SimpleFont("Arial", 9);
                }
                if (lastMaxPutOiMacro > 0)
                {
                    Draw.Line(this, "MaxPutOi_Macro", false, sessionStartTime, lastMaxPutOiMacro, futureTime, lastMaxPutOiMacro, Brushes.Maroon, DashStyleHelper.DashDot, 3);
                    var t = Draw.Text(this, "Txt_MaxPutOi_Macro", "Max Put OI MACRO", 5, lastMaxPutOiMacro - 20, Brushes.Maroon);
                    if (t != null) t.Font = new Gui.Tools.SimpleFont("Arial", 9);
                }
            }

            needsRedraw = false;
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private GammaLevelsIndicator[] cacheGammaLevelsIndicator;
		public GammaLevelsIndicator GammaLevelsIndicator(string fileName, int refreshInterval, GammaDisplayMode displayMode)
		{
			return GammaLevelsIndicator(Input, fileName, refreshInterval, displayMode);
		}

		public GammaLevelsIndicator GammaLevelsIndicator(ISeries<double> input, string fileName, int refreshInterval, GammaDisplayMode displayMode)
		{
			if (cacheGammaLevelsIndicator != null)
				for (int idx = 0; idx < cacheGammaLevelsIndicator.Length; idx++)
					if (cacheGammaLevelsIndicator[idx] != null && cacheGammaLevelsIndicator[idx].FileName == fileName && cacheGammaLevelsIndicator[idx].RefreshInterval == refreshInterval && cacheGammaLevelsIndicator[idx].DisplayMode == displayMode && cacheGammaLevelsIndicator[idx].EqualsInput(input))
						return cacheGammaLevelsIndicator[idx];
			return CacheIndicator<GammaLevelsIndicator>(new GammaLevelsIndicator(){ FileName = fileName, RefreshInterval = refreshInterval, DisplayMode = displayMode }, input, ref cacheGammaLevelsIndicator);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.GammaLevelsIndicator GammaLevelsIndicator(string fileName, int refreshInterval, GammaDisplayMode displayMode)
		{
			return indicator.GammaLevelsIndicator(Input, fileName, refreshInterval, displayMode);
		}

		public Indicators.GammaLevelsIndicator GammaLevelsIndicator(ISeries<double> input , string fileName, int refreshInterval, GammaDisplayMode displayMode)
		{
			return indicator.GammaLevelsIndicator(input, fileName, refreshInterval, displayMode);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.GammaLevelsIndicator GammaLevelsIndicator(string fileName, int refreshInterval, GammaDisplayMode displayMode)
		{
			return indicator.GammaLevelsIndicator(Input, fileName, refreshInterval, displayMode);
		}

		public Indicators.GammaLevelsIndicator GammaLevelsIndicator(ISeries<double> input , string fileName, int refreshInterval, GammaDisplayMode displayMode)
		{
			return indicator.GammaLevelsIndicator(input, fileName, refreshInterval, displayMode);
		}
	}
}

#endregion
