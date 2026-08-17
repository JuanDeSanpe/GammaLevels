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
        private double lastKnownNqPrice = 0;
        private DateTime sessionStartTime;
        private DateTime lastKnownTime;

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

                var levels0DTE = GammaLevelsAnalyzer.Analyze(strikes0DTE);
                var levelsMacro = GammaLevelsAnalyzer.Analyze(strikesMacro);

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
                                DateTime futureTime = lastKnownTime.AddDays(5);
                                
                                // 0DTE tags and drawing (Líneas finas punteadas)
                                string call0DTETag = "CallWall_0DTE_" + sessionStartTime.ToString("yyyyMMdd") + "_" + levels0DTE.CallWallStrike;
                                string put0DTETag = "PutWall_0DTE_" + sessionStartTime.ToString("yyyyMMdd") + "_" + levels0DTE.PutWallStrike;
                                string flip0DTETag = "GammaFlip_0DTE_" + sessionStartTime.ToString("yyyyMMdd") + "_" + levels0DTE.GammaFlipStrike;

                                if (levels0DTE.CallWallStrike > 0) Draw.Line(this, call0DTETag, false, sessionStartTime, callWall0DTE_Nq, futureTime, callWall0DTE_Nq, CallWallColor, DashStyleHelper.Dash, 2);
                                if (levels0DTE.PutWallStrike > 0) Draw.Line(this, put0DTETag, false, sessionStartTime, putWall0DTE_Nq, futureTime, putWall0DTE_Nq, PutWallColor, DashStyleHelper.Dash, 2);
                                if (levels0DTE.GammaFlipStrike > 0) Draw.Line(this, flip0DTETag, false, sessionStartTime, flip0DTE_Nq, futureTime, flip0DTE_Nq, GammaFlipColor, DashStyleHelper.Dash, 2);

                                // Macro tags and drawing (Líneas gruesas sólidas)
                                string callMacroTag = "CallWall_Macro_" + sessionStartTime.ToString("yyyyMMdd") + "_" + levelsMacro.CallWallStrike;
                                string putMacroTag = "PutWall_Macro_" + sessionStartTime.ToString("yyyyMMdd") + "_" + levelsMacro.PutWallStrike;
                                string flipMacroTag = "GammaFlip_Macro_" + sessionStartTime.ToString("yyyyMMdd") + "_" + levelsMacro.GammaFlipStrike;

                                if (levelsMacro.CallWallStrike > 0) Draw.Line(this, callMacroTag, false, sessionStartTime, callWallMacro_Nq, futureTime, callWallMacro_Nq, CallWallColor, DashStyleHelper.Solid, 4);
                                if (levelsMacro.PutWallStrike > 0) Draw.Line(this, putMacroTag, false, sessionStartTime, putWallMacro_Nq, futureTime, putWallMacro_Nq, PutWallColor, DashStyleHelper.Solid, 4);
                                if (levelsMacro.GammaFlipStrike > 0) Draw.Line(this, flipMacroTag, false, sessionStartTime, flipMacro_Nq, futureTime, flipMacro_Nq, GammaFlipColor, DashStyleHelper.Solid, 4);

                                string regime0DTEText = levels0DTE.TotalNetGex > 0 ? "0DTE Régimen: POSITIVO (Baja Volatilidad)" : "0DTE Régimen: NEGATIVO (Alta Volatilidad)";
                                string regimeMacroText = levelsMacro.TotalNetGex > 0 ? "MACRO Régimen: POSITIVO (Baja Volatilidad)" : "MACRO Régimen: NEGATIVO (Alta Volatilidad)";
                                
                                Brush hudColor = levels0DTE.TotalNetGex > 0 ? Brushes.LimeGreen : Brushes.Red;
                                string hudText = string.Format("--- DEBUG INFO ---\nUnderlying: {4}\nRatio: {5:F2}\nCallStrike: {6}\nPutStrike: {7}\n\n--- GEX MACRO ---\nNet GEX: {0:N0}\n{1}\n\n--- GEX 0DTE ---\nNet GEX: {2:N0}\n{3}", levelsMacro.TotalNetGex, regimeMacroText, levels0DTE.TotalNetGex, regime0DTEText, parsedData.UnderlyingPrice, ratio, levelsMacro.CallWallStrike, levelsMacro.PutWallStrike);
                                
                                Draw.TextFixed(this, "GammaHUD", hudText, TextPosition.TopRight, hudColor, new Gui.Tools.SimpleFont("Arial", 12) { Bold = true }, Brushes.Transparent, Brushes.Transparent, 0);

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
            if (CurrentBar >= 0)
            {
                lastKnownNqPrice = Close[0];
                lastKnownTime = Time[0];
                
                if (Bars.IsFirstBarOfSession)
                {
                    sessionStartTime = Time[0];
                }
                else if (sessionStartTime == DateTime.MinValue)
                {
                    // Fallback si el indicador se carga a mitad de sesión
                    sessionStartTime = Time[0].Date;
                }
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
