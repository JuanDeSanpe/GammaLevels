using System;
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

namespace NinjaTrader.NinjaScript.Indicators
{
    public class GammaLevelsIndicator : Indicator
    {
        private System.Threading.Timer refreshTimer;
        private string folderPath;

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
                Calculate = Calculate.OnBarClose; // Minimal impact since we use an async timer
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                FileName = "2026-08-17-StockAndOptionQuoteForQQQ.csv";
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
                // Start background timer
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
            try
            {
                string fullPath = Path.Combine(folderPath, FileName);
                var strikes = GammaDataParser.ParseCSV(fullPath, msg => Print(msg));
                var levels = GammaLevelsAnalyzer.Analyze(strikes);

                if (levels.IsValid)
                {
                    // Ensure we are attached to a chart and can invoke on the UI thread
                    if (ChartControl != null && ChartControl.Dispatcher != null)
                    {
                        ChartControl.Dispatcher.InvokeAsync(new Action(() => 
                        {
                            try
                            {
                                Draw.HorizontalLine(this, "CallWall", levels.CallWallStrike, CallWallColor);
                                Draw.HorizontalLine(this, "PutWall", levels.PutWallStrike, PutWallColor);
                                Draw.HorizontalLine(this, "GammaFlip", levels.GammaFlipStrike, GammaFlipColor);
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
            // Indicator logic runs asynchronously in the TimerCallback
        }
    }
}
