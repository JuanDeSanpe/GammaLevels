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
    public enum RatioCalculationMode
    {
        Fixed,
        Smoothed
    }

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
        private DispatcherTimer timer;

        // Variables para pasar datos del hilo secundario al OnBarUpdate
        private double lastCall0DTE = 0;
        private double lastPut0DTE = 0;
        private double lastFlip0DTE = 0;
        private double lastCallMacro = 0;
        private double lastPutMacro = 0;
        private double lastFlipMacro = 0;
        private bool needsRedraw = false;
        private double savedRatio = 0;
        private string label0DTE = "";
        private bool isTimerStarted = false;

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

        [NinjaScriptProperty]
        [Display(Name="Ratio Calculation Mode", Description="Fixed: Fija el ratio una vez. Smoothed: Ajuste dinámico suave.", Order=6, GroupName="Parameters")]
        public RatioCalculationMode RatioMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Display Mode", Description="Mostrar 0DTE, Macro o ambos", Order=7, GroupName="Parameters")]
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
                CallWallColor = Brushes.LimeGreen;
                PutWallColor = Brushes.Red;
                GammaFlipColor = Brushes.White;
                RatioMode = RatioCalculationMode.Fixed;
                DisplayMode = GammaDisplayMode.Both;
            }
            else if (State == State.Configure)
            {
                folderPath = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "bin", "Custom", "Indicators", "TOS Opciones", "Archivos Cadena de Opciones");
            }
            else if (State == State.DataLoaded)
            {
                if (refreshTimer == null)
                {
                    // Inicializar parado (Infinite)
                    refreshTimer = new System.Threading.Timer(TimerCallback, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                }
            }
            else if (State == State.Realtime)
            {
                // Cuando NinjaTrader termina de cargar todo el historial, ¡dispara el timer al instante!
                if (refreshTimer != null && !isTimerStarted)
                {
                    refreshTimer.Change(0, RefreshInterval * 1000);
                    isTimerStarted = true;
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
                    double currentRatio = lastKnownNqPrice / parsedData.UnderlyingPrice;
                    double ratioToUse = currentRatio;

                    if (RatioMode == RatioCalculationMode.Fixed)
                    {
                        if (savedRatio == 0) savedRatio = currentRatio;
                        ratioToUse = savedRatio;
                    }
                    else if (RatioMode == RatioCalculationMode.Smoothed)
                    {
                        if (savedRatio == 0) savedRatio = currentRatio;
                        else savedRatio = (savedRatio * 0.90) + (currentRatio * 0.10); // EMA (10% peso al nuevo precio)
                        ratioToUse = savedRatio;
                    }

                    // 0DTE
                    double callWall0DTE_Nq = levels0DTE.CallWallStrike * ratioToUse;
                    double putWall0DTE_Nq = levels0DTE.PutWallStrike * ratioToUse;
                    double flip0DTE_Nq = levels0DTE.GammaFlipStrike * ratioToUse;

                    // Macro
                    double callWallMacro_Nq = levelsMacro.CallWallStrike * ratioToUse;
                    double putWallMacro_Nq = levelsMacro.PutWallStrike * ratioToUse;
                    double flipMacro_Nq = levelsMacro.GammaFlipStrike * ratioToUse;

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
                                label0DTE = validStrikes.Count > 0 ? validStrikes.Min(s => s.ExpirationDate).ToString("dd/MMM") : "0DTE";
                                needsRedraw = true;

                                string hudText = "";
                                if (DisplayMode == GammaDisplayMode.Both || DisplayMode == GammaDisplayMode.OnlyMacro)
                                    hudText += "MACRO: " + (levelsMacro.TotalNetGex > 0 ? "POS (Baja Vol)" : "NEG (Alta Vol)") + "\n";
                                if (DisplayMode == GammaDisplayMode.Both || DisplayMode == GammaDisplayMode.Only0DTE)
                                    hudText += "0DTE: " + (levels0DTE.TotalNetGex > 0 ? "POS (Baja Vol)" : "NEG (Alta Vol)");
                                
                                Draw.TextFixed(this, "GammaHUD", hudText.Trim(), TextPosition.TopRight, Brushes.LightGray, new Gui.Tools.SimpleFont("Arial", 11) { Bold = true }, Brushes.Transparent, Brushes.Transparent, 0);

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
            if (needsRedraw && sessionStartTime != DateTime.MinValue)
            {
                DateTime futureTime = lastKnownTime.AddDays(5);
                
                // 0DTE
                if (DisplayMode == GammaDisplayMode.Both || DisplayMode == GammaDisplayMode.Only0DTE)
                {
                    if (lastCall0DTE > 0) { 
                        Draw.Line(this, "CallWall_0DTE_Live", false, sessionStartTime, lastCall0DTE, futureTime, lastCall0DTE, CallWallColor, DashStyleHelper.Dash, 2); 
                        var t1 = Draw.Text(this, "Txt_Call_0DTE", "Call 0DTE (" + label0DTE + ")", -10, lastCall0DTE + 10, CallWallColor); 
                        if (t1 != null) t1.Font = new Gui.Tools.SimpleFont("Arial", 10);
                    }
                    if (lastPut0DTE > 0) { 
                        Draw.Line(this, "PutWall_0DTE_Live", false, sessionStartTime, lastPut0DTE, futureTime, lastPut0DTE, PutWallColor, DashStyleHelper.Dash, 2); 
                        var t2 = Draw.Text(this, "Txt_Put_0DTE", "Put 0DTE (" + label0DTE + ")", -10, lastPut0DTE - 10, PutWallColor); 
                        if (t2 != null) t2.Font = new Gui.Tools.SimpleFont("Arial", 10);
                    }
                    if (lastFlip0DTE > 0) { 
                        Draw.Line(this, "GammaFlip_0DTE_Live", false, sessionStartTime, lastFlip0DTE, futureTime, lastFlip0DTE, GammaFlipColor, DashStyleHelper.Dash, 2); 
                        var t3 = Draw.Text(this, "Txt_Flip_0DTE", "Flip 0DTE (" + label0DTE + ")", -10, lastFlip0DTE + 10, GammaFlipColor); 
                        if (t3 != null) t3.Font = new Gui.Tools.SimpleFont("Arial", 10);
                    }
                }
                else
                {
                    RemoveDrawObject("CallWall_0DTE_Live"); RemoveDrawObject("PutWall_0DTE_Live"); RemoveDrawObject("GammaFlip_0DTE_Live");
                    RemoveDrawObject("Txt_Call_0DTE"); RemoveDrawObject("Txt_Put_0DTE"); RemoveDrawObject("Txt_Flip_0DTE");
                }

                // MACRO
                if (DisplayMode == GammaDisplayMode.Both || DisplayMode == GammaDisplayMode.OnlyMacro)
                {
                    if (lastCallMacro > 0) { 
                        Draw.Line(this, "CallWall_Macro_Live", false, sessionStartTime, lastCallMacro, futureTime, lastCallMacro, CallWallColor, DashStyleHelper.Solid, 4); 
                        var t4 = Draw.Text(this, "Txt_Call_Macro", "Call MACRO", -10, lastCallMacro + 25, CallWallColor); 
                        if (t4 != null) t4.Font = new Gui.Tools.SimpleFont("Arial", 10);
                    }
                    if (lastPutMacro > 0) { 
                        Draw.Line(this, "PutWall_Macro_Live", false, sessionStartTime, lastPutMacro, futureTime, lastPutMacro, PutWallColor, DashStyleHelper.Solid, 4); 
                        var t5 = Draw.Text(this, "Txt_Put_Macro", "Put MACRO", -10, lastPutMacro - 25, PutWallColor); 
                        if (t5 != null) t5.Font = new Gui.Tools.SimpleFont("Arial", 10);
                    }
                    if (lastFlipMacro > 0) { 
                        Draw.Line(this, "GammaFlip_Macro_Live", false, sessionStartTime, lastFlipMacro, futureTime, lastFlipMacro, GammaFlipColor, DashStyleHelper.Solid, 4); 
                        var t6 = Draw.Text(this, "Txt_Flip_Macro", "Flip MACRO", -10, lastFlipMacro + 25, GammaFlipColor); 
                        if (t6 != null) t6.Font = new Gui.Tools.SimpleFont("Arial", 10);
                    }
                }
                else
                {
                    RemoveDrawObject("CallWall_Macro_Live"); RemoveDrawObject("PutWall_Macro_Live"); RemoveDrawObject("GammaFlip_Macro_Live");
                    RemoveDrawObject("Txt_Call_Macro"); RemoveDrawObject("Txt_Put_Macro"); RemoveDrawObject("Txt_Flip_Macro");
                }
                
                needsRedraw = false;
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
		public GammaLevelsIndicator GammaLevelsIndicator(string fileName, int refreshInterval, RatioCalculationMode ratioMode, GammaDisplayMode displayMode)
		{
			return GammaLevelsIndicator(Input, fileName, refreshInterval, ratioMode, displayMode);
		}

		public GammaLevelsIndicator GammaLevelsIndicator(ISeries<double> input, string fileName, int refreshInterval, RatioCalculationMode ratioMode, GammaDisplayMode displayMode)
		{
			if (cacheGammaLevelsIndicator != null)
				for (int idx = 0; idx < cacheGammaLevelsIndicator.Length; idx++)
					if (cacheGammaLevelsIndicator[idx] != null && cacheGammaLevelsIndicator[idx].FileName == fileName && cacheGammaLevelsIndicator[idx].RefreshInterval == refreshInterval && cacheGammaLevelsIndicator[idx].RatioMode == ratioMode && cacheGammaLevelsIndicator[idx].DisplayMode == displayMode && cacheGammaLevelsIndicator[idx].EqualsInput(input))
						return cacheGammaLevelsIndicator[idx];
			return CacheIndicator<GammaLevelsIndicator>(new GammaLevelsIndicator(){ FileName = fileName, RefreshInterval = refreshInterval, RatioMode = ratioMode, DisplayMode = displayMode }, input, ref cacheGammaLevelsIndicator);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.GammaLevelsIndicator GammaLevelsIndicator(string fileName, int refreshInterval, RatioCalculationMode ratioMode, GammaDisplayMode displayMode)
		{
			return indicator.GammaLevelsIndicator(Input, fileName, refreshInterval, ratioMode, displayMode);
		}

		public Indicators.GammaLevelsIndicator GammaLevelsIndicator(ISeries<double> input , string fileName, int refreshInterval, RatioCalculationMode ratioMode, GammaDisplayMode displayMode)
		{
			return indicator.GammaLevelsIndicator(input, fileName, refreshInterval, ratioMode, displayMode);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.GammaLevelsIndicator GammaLevelsIndicator(string fileName, int refreshInterval, RatioCalculationMode ratioMode, GammaDisplayMode displayMode)
		{
			return indicator.GammaLevelsIndicator(Input, fileName, refreshInterval, ratioMode, displayMode);
		}

		public Indicators.GammaLevelsIndicator GammaLevelsIndicator(ISeries<double> input , string fileName, int refreshInterval, RatioCalculationMode ratioMode, GammaDisplayMode displayMode)
		{
			return indicator.GammaLevelsIndicator(input, fileName, refreshInterval, ratioMode, displayMode);
		}
	}
}

#endregion
