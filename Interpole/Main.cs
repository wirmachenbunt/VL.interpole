#region usings
using System;
using System.ComponentModel.Composition;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Drawing;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;

using VVVV.Core.Logging;
#endregion usings

namespace VVVV.Nodes
{
    #region PluginInfo
    [PluginInfo(Name = "Interpole",
                Category = "GUI",
                Help = "Clip Timeline",
                Tags = "c#",
                AutoEvaluate = true)]
    #endregion PluginInfo
    public partial class Interpole : UserControl, IPluginEvaluate
    {
        #region fields & pins

        [Config("Framerate",DefaultValue = 50 )]
        public IDiffSpread<int> Framerate;

        [Input("Play")]
        public IDiffSpread<bool> FPlay;

        [Config("IsLoop")]
        public IDiffSpread<bool> FIsLoop;

        [Input("Timecode")]
        public IDiffSpread<TimeCode> FTimecode;

        [Input("Set Timecode")]
        public IDiffSpread<bool> FSetTimecode;

        [Input("Hide Window")]
        public IDiffSpread<bool> FHideWindow;

        [Input("ClipXMLIn")]
        public IDiffSpread<string> ClipXMLIn;

        [Config("Marker")]
        public IDiffSpread<Int32> FMarker;

        [Config("LoopRegion")]
        public IDiffSpread<Int32> FLoopRegion;

        [Config("ClipXML")]
        public IDiffSpread<string> ClipXML;

        [Output("Clips")]
        public ISpread<Interpole> ClipsOut;

        [Output("ClipXML")]
        public ISpread<string> ClipXMLOut;

        [Import()]
        public ILogger FLogger;

        [Import()]
        IPluginHost FHost;

        [Import]
        IHDEHost FHdeHost;

        [Import]
        IPluginHost2 FPluginHost;

        bool initialized = false; // controlling init of plugin
        public bool play = false;
        public bool needsUpdate = true;

        Rectangle playHeadInvalidate;

        #endregion fields & pins

        #region constructor and init

        public Interpole()
        {
            //setup the gui
            InitializeComponent();
            myplaybar.Bounds = new Rectangle(new Point(0 - StagePos.X, 0), new Size(this.Width, 30)); // setup playbar
        }

        void InitializeComponent()
        {
            //clear controls in case init is called multiple times
            Controls.Clear();

            Paint += MainDraw; //paint event
            SetStyle(ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

            //bind events
            MouseMove += Form1_MouseMove; //mouse move event
            MouseDoubleClick += Form1_MouseDoubleClick; //mouse click event
            MouseDown += Form1_MouseDown; //mouse down event
            MouseUp += Form1_MouseUp;
            MouseWheel += Form1_MouseWheel;
            KeyDown += Form_Keydown;

            
        }

        #endregion constructor and init

        public void InitInterpole()
        {
            //hide window programatically
            if ( FHideWindow[0]) HideWindow();

            if (FMarker.SliceCount != 9) FMarker.SliceCount = 9;

            if (FLoopRegion.SliceCount != 2) FLoopRegion.SliceCount = 2;
            
            if (!initialized)
            {
                try
                {
                    clipList = DeserializeClipList(ClipXML[0]); // load xml from config pin

                    undoXML.Push(ClipXML[0]); //add first undo step

                    ApplyTimeZoom(); // scale to zoom

                    FLogger.Log(LogType.Debug, "Interpole successful initialized");

                    this.DoubleBuffered = true; //not sure if this is actually nessecary

                    Invalidate(); // redraw all
                }
                catch
                {
                    FLogger.Log(LogType.Debug, "Loading clip list failed :(");
                    clipList = new List<Clip>(); // fixes many bugs when interpole is fresh and empty
                }

                initialized = true;
            }
        }

        // PLAYBACK METHODE
        public void Play()
        {

            if(FPlay.IsChanged)
            {
                play = FPlay[0];
            }

            if (play && !FSetTimecode[0])
            {
                playPosFrames += 1;

                //PlaybackMoveStage(); // scroll stage when playback

                int pos = playPosFrames/timeZoom;

                playHeadInvalidate = new Rectangle(new Point(pos-10 + StagePos.X, 0), new Size(20,this.Height)); //invalidate area for playhead
                Invalidate(new Rectangle(new Point(mytimecode.Bounds.X + StagePos.X, mytimecode.Bounds.Y),mytimecode.Bounds.Size)); // invalidate timecode display
                Invalidate(playHeadInvalidate); // invalidate playhead

            } else if (FSetTimecode[0] && FTimecode[0] != null) // control playback by external timecode
            {
                play = false;
                playPosFrames = FTimecode[0].Frames + (FTimecode[0].Seconds * Framerate[0]) + (FTimecode[0].Minutes * 60 * Framerate[0]) + (FTimecode[0].Hours * 60 * 60 * Framerate[0]);
                PlaybackMoveStage(); // scroll stage when playback
                Invalidate();
            }

            //activate loop region
            if (!FSetTimecode[0] && FIsLoop[0] && 
                playPosFrames >= FLoopRegion[1] && 
                play && FLoopRegion[1] > 0 &&
                FLoopRegion[1] > FLoopRegion[0]) playPosFrames = FLoopRegion[0];
        }

        //called when data for any output pin is requested
        public void Evaluate(int SpreadMax)
        {
            InitInterpole();
            Play();
            
            ClipsOut.SliceCount = 1; // set output for additional nodes
            ClipsOut[0] = this;

            //iaa special feature
            if(ClipXMLIn.IsChanged && ClipXMLIn[0].Length > 0)
            {
                clipList = DeserializeClipList(ClipXMLIn[0]);
                Invalidate();
            }

            //if(needsUpdate) FLogger.Log(LogType.Debug, "update");          
        }
    }
}