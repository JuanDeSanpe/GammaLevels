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

namespace NinjaTrader.NinjaScript.Indicators
{
    public class GammaLevelsIndicator : Indicator
    {
        private System.Threading.Timer refreshTimer;
        private string folderPath;
        private double lastKnownNqPrice;
        private DateTime lastKnownTime;
        private DateTime sessionStartTime;
        private DispatcherTimer timer;

        // Variables para pasar datos del hilo secundario al OnBarUpdate
        private double lastCall0DTE = 0;
        private double lastPut0DTE = 0;
        private double lastFlip0DTE = 0;
        private double lastCallMacro = 0;
        private double lastPutMacro = 0;
        private double lastFlipMacro = 0;

        [NinjaScriptProperty]
        [Display(Name="File Name", Description="Name of the CSV file in Archivos Cadena de Opciones folder", Order=1, GroupName="Parameters")]
        public string FileName { get; set; }

        [NinjaScriptProperty]
        [Range(1, 60)]
        [Display(Name="Refresh Interval (Seconds)", Description="How often to read the file", Order=2, GroupName="Parameters")]
        public int RefreshInterval { get; set; }

        [XmlIgnore]
        [Display(Name="Call Wall Color", Order=3, GroupName="Parameters")]
        public Brush CallWallColor { get; set; }

        [Browsable(false)]
        public string CallWallColorSerializable
        {
            get { return Serialize.BrushToString(CallWallColor); }
            set { CallWallColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name="Put Wall Color", Order=4, GroupName="Parameters")]
        public Brush PutWallColor { get; set; }

        [Browsable(false)]
        public string PutWallColorSerializable
        {
            get { return Serialize.BrushToString(PutWallColor); }
            set { PutWallColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name="Gamma Flip Color", Order=5, GroupName="Parameters")]
        public Brush GammaFlipColor { get; set; }

        [Browsable(false)]
        public string GammaFlipColorSerializable
        {
            get { return Serialize.BrushToString(GammaFlipColor); }
            set { GammaFlipColor = Serialize.StringToBrush(value); }
        }

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
                CallWallColor = Brushes.LimeGreen;
                PutWallColor = Brushes.Red;
                GammaFlipColor = Brushes.White;
            }
            else if (State == State.Configure)
            {
                folderPath = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "bin", "Custom", "Indicators", "TOS Opciones", "Archivos Cadena de Opciones");
            }
            else if (State == State.DataLoaded)
            {
                if (refreshTimer == null)
                {
                    refreshTimer = new System.Threading.Timer(TimerCallback, null, 0, RefreshInterval * 1000);
                }
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
            if (lastKnownNqPrice == 0 || sessionStartTime == DateTime.MinValue) return;

            try
            {
                string fullPath = Path.Combine(folderPath, FileName);
                var parsedData = GammaDataParser.ParseCSV(fullPath, msg => Print(msg));
                var validStrikes = parsedData.Strikes.Where(s => s.ExpirationDate > DateTime.MinValue).ToList();
                List<GammaStrikeModel> strikes0DTE = parsedData.Strikes;
                List<GammaStrikeModel> strikesMacro = parsedData.Strikes;

                if (validStrikes.Count > 0)
                {
                    DateTime minDate = validStrikes.Min(s => s.ExpirationDate);
                    strikes0DTE = validStrikes.Where(s => s.ExpirationDate == minDate).ToList();
                }
                else
                {
                    // Si no se pudo parsear ninguna fecha (ej. por exportación RTD sin columna Exp),
                    // asumimos que todos los strikes pertenecen al 0DTE como fallback.
                    strikes0DTE = parsedData.Strikes;
                }

                var levels0DTE = GammaLevelsAnalyzer.Analyze(strikes0DTE, parsedData.UnderlyingPrice);
                var levelsMacro = GammaLevelsAnalyzer.Analyze(strikesMacro, parsedData.UnderlyingPrice);

                if ((levels0DTE.IsValid || levelsMacro.IsValid) && parsedData.UnderlyingPrice > 0)
                {
                    double ratio = lastKnownNqPrice / parsedData.UnderlyingPrice;
                    
                    // 0DTE
                    double callWall0DTE_Nq = levels0DTE.CallWallStrike * ratio;
                    double putWall0DTE_Nq = levels0DTE.PutWallStrike * ratio;
                    double flip0DTE_Nq = levels0DTE.GammaFlipStrike * ratio;

                    // Macro
                    double callWallMacro_Nq = levelsMacro.CallWallStrike * ratio;
                    double putWallMacro_Nq = levelsMacro.PutWallStrike * ratio;
                    double flipMacro_Nq = levelsMacro.GammaFlipStrike * ratio;

                    if (ChartControl != null && ChartControl.Dispatcher != null)
                    {
                        ChartControl.Dispatcher.InvokeAsync(new Action(() => 
                        {
                            try
                            {
                                lastCall0DTE = callWall0DTE_Nq;
                                lastPut0DTE = putWall0DTE_Nq;
                                lastFlip0DTE = flip0DTE_Nq;
                                lastCallMacro = callWallMacro_Nq;
                                lastPutMacro = putWallMacro_Nq;
                                lastFlipMacro = flipMacro_Nq;

                                string regime0DTEText = levels0DTE.TotalNetGex > 0 ? "0DTE: POSITIVO (Baja Vol)" : "0DTE: NEGATIVO (Alta Vol)";
                                string regimeMacroText = levelsMacro.TotalNetGex > 0 ? "MACRO: POSITIVO (Baja Vol)" : "MACRO: NEGATIVO (Alta Vol)";
                                
                                Brush hudColor = levels0DTE.TotalNetGex > 0 ? Brushes.LimeGreen : Brushes.Red;
                                string hudText = string.Format(
                                    "--- DEBUG ---\nUnderlying: {0}\nRatio: {1:F2}\nNQ Price: {2:F2}\n\n--- MACRO Strikes ---\nCallWall: {3} -> NQ: {4:F2}\nPutWall: {5} -> NQ: {6:F2}\nFlip: {7} -> NQ: {8:F2}\nGEX: {9:N0} {10}\n\n--- 0DTE Strikes ---\nCallWall: {11} -> NQ: {12:F2}\nPutWall: {13} -> NQ: {14:F2}\nFlip: {15} -> NQ: {16:F2}\nGEX: {17:N0} {18}\n\n--- DRAW STATE ---\nlastCallMacro: {19:F2}\nlastPutMacro: {20:F2}\nlastFlipMacro: {21:F2}\nsessionStart: {22}",
                                    parsedData.UnderlyingPrice, ratio, lastKnownNqPrice,
                                    levelsMacro.CallWallStrike, callWallMacro_Nq,
                                    levelsMacro.PutWallStrike, putWallMacro_Nq,
                                    levelsMacro.GammaFlipStrike, flipMacro_Nq,
                                    levelsMacro.TotalNetGex, regimeMacroText,
                                    levels0DTE.CallWallStrike, callWall0DTE_Nq,
                                    levels0DTE.PutWallStrike, putWall0DTE_Nq,
                                    levels0DTE.GammaFlipStrike, flip0DTE_Nq,
                                    levels0DTE.TotalNetGex, regime0DTEText,
                                    lastCallMacro, lastPutMacro, lastFlipMacro,
                                    sessionStartTime.ToString("yyyy-MM-dd HH:mm"));
                                
                                Draw.TextFixed(this, "GammaHUD", hudText, TextPosition.TopRight, hudColor, new Gui.Tools.SimpleFont("Arial", 11) { Bold = true }, Brushes.Transparent, Brushes.Transparent, 0);

                                ForceRefresh();
                            }
                            catch (Exception drawEx)
                            {
                                Print("GammaLevelsIndicator Draw Error: " + drawEx.Message);
                            }
                        }));
                    }
                }
            }
            catch (Exception ex)
            {
                Print("GammaLevelsIndicator Timer Error: " + ex.Message);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1) return;
            
            lastKnownNqPrice = Close[0];
            lastKnownTime = Time[0];

            if (Bars.IsFirstBarOfSession)
                sessionStartTime = Time[0];
            else if (sessionStartTime == DateTime.MinValue)
                sessionStartTime = Time[0].Date;
                
            // Dibujamos las lineas desde OnBarUpdate (hilo nativo de NinjaTrader)
            if (sessionStartTime != DateTime.MinValue)
            {
                DateTime futureTime = lastKnownTime.AddDays(5);
                
                if (lastCall0DTE > 0) Draw.Line(this, "CallWall_0DTE_Live", false, sessionStartTime, lastCall0DTE, futureTime, lastCall0DTE, CallWallColor, DashStyleHelper.Dash, 2);
                if (lastPut0DTE > 0) Draw.Line(this, "PutWall_0DTE_Live", false, sessionStartTime, lastPut0DTE, futureTime, lastPut0DTE, PutWallColor, DashStyleHelper.Dash, 2);
                if (lastFlip0DTE > 0) Draw.Line(this, "GammaFlip_0DTE_Live", false, sessionStartTime, lastFlip0DTE, futureTime, lastFlip0DTE, GammaFlipColor, DashStyleHelper.Dash, 2);

                if (lastCallMacro > 0) Draw.Line(this, "CallWall_Macro_Live", false, sessionStartTime, lastCallMacro, futureTime, lastCallMacro, CallWallColor, DashStyleHelper.Solid, 4);
                if (lastPutMacro > 0) Draw.Line(this, "PutWall_Macro_Live", false, sessionStartTime, lastPutMacro, futureTime, lastPutMacro, PutWallColor, DashStyleHelper.Solid, 4);
                if (lastFlipMacro > 0) Draw.Line(this, "GammaFlip_Macro_Live", false, sessionStartTime, lastFlipMacro, futureTime, lastFlipMacro, GammaFlipColor, DashStyleHelper.Solid, 4);
            }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private GammaLevelsIndicator[] cacheGammaLevelsIndicator;
		public GammaLevelsIndicator GammaLevelsIndicator(string fileName, int refreshInterval)
		{
			return GammaLevelsIndicator(Input, fileName, refreshInterval);
		}

		public GammaLevelsIndicator GammaLevelsIndicator(ISeries<double> input, string fileName, int refreshInterval)
		{
			if (cacheGammaLevelsIndicator != null)
				for (int idx = 0; idx < cacheGammaLevelsIndicator.Length; idx++)
					if (cacheGammaLevelsIndicator[idx] != null && cacheGammaLevelsIndicator[idx].FileName == fileName && cacheGammaLevelsIndicator[idx].RefreshInterval == refreshInterval && cacheGammaLevelsIndicator[idx].EqualsInput(input))
						return cacheGammaLevelsIndicator[idx];
			return CacheIndicator<GammaLevelsIndicator>(new GammaLevelsIndicator(){ FileName = fileName, RefreshInterval = refreshInterval }, input, ref cacheGammaLevelsIndicator);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.GammaLevelsIndicator GammaLevelsIndicator(string fileName, int refreshInterval)
		{
			return indicator.GammaLevelsIndicator(Input, fileName, refreshInterval);
		}

		public Indicators.GammaLevelsIndicator GammaLevelsIndicator(ISeries<double> input , string fileName, int refreshInterval)
		{
			return indicator.GammaLevelsIndicator(input, fileName, refreshInterval);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.GammaLevelsIndicator GammaLevelsIndicator(string fileName, int refreshInterval)
		{
			return indicator.GammaLevelsIndicator(Input, fileName, refreshInterval);
		}

		public Indicators.GammaLevelsIndicator GammaLevelsIndicator(ISeries<double> input , string fileName, int refreshInterval)
		{
			return indicator.GammaLevelsIndicator(input, fileName, refreshInterval);
		}
	}
}

#endregion
