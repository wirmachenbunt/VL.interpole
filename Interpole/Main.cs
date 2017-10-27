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
using System.Reflection;
using System.Diagnostics;
#endregion usings

namespace VVVV.Nodes
{
    #region PluginInfo
    [PluginInfo(Name = "Interpole",
                Category = "Interpole",
                Help = "Clip Timeline",
                Tags = "c#",
                AutoEvaluate = true)]
    #endregion PluginInfo
    public partial class Interpole : UserControl, IPluginEvaluate
    {
        #region fields & pins

        [Config("Framerate", DefaultValue = 50 )]
        public IDiffSpread<int> Framerate;

        [Input("Play")]
        public IDiffSpread<bool> FPlay;

        [Config("IsLoop")]
        public IDiffSpread<bool> FIsLoop;

        [Input("Timecode")]
        public IDiffSpread<TimeCode> FTimecode;

        [Input("Set Timecode")]
        public IDiffSpread<bool> FSetTimecode;

        [Input("Show Window")]
        public IDiffSpread<bool> FShowWindow;

        [Input("External Clock", IsSingle = true)]
        public IDiffSpread<int> FExternalClock;

        [Input("Set External Clock")]
        public IDiffSpread<bool> FSetExternalClock;

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
        int UpdateCounter = 0;
        bool UI_Lock = false;

        bool lastShowWindow = false;
        int lastExternalClock = 0;

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
            if (!FShowWindow[0] == lastShowWindow) {
                if (FShowWindow[0]) ShowWindow();
                else HideWindow();
                lastShowWindow = FShowWindow[0];
            }

            if (FMarker.SliceCount != 9) FMarker.SliceCount = 9;

            if (FLoopRegion.SliceCount != 2) FLoopRegion.SliceCount = 2;
            
            if (!initialized)
            {
                try
                {
                    clipList = DeserializeClipList(ClipXML[0]); // load xml from config pin

                    ClipXMLOut[0] = ClipXML[0];

                    undoXML.Push(ClipXML[0]); //add first undo step

                    ApplyTimeZoom(); // scale to zoom

                    System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
                    FLogger.Log(LogType.Debug, "Interpole " + fvi.FileVersion + " successful initialized");

                    this.DoubleBuffered = true; //not sure if this is actually nessecary

                    Invalidate(); // redraw all
                } 
                catch (Exception e)
                {
                    FLogger.Log(LogType.Error, "Loading clip list failed :( " + e);
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
                if (FSetExternalClock[0])
                {
                    int diff = FExternalClock[0] - lastExternalClock;
                    playPosFrames += diff > 0 ? diff : 0;

                    if (lastExternalClock != FExternalClock[0]) lastExternalClock = FExternalClock[0];
                }
                else playPosFrames += 1;

                PlaybackMoveStage(); // scroll stage when playback

                int pos = playPosFrames/timeZoom;

                playHeadInvalidate = new Rectangle(new Point(pos-10 + StagePos.X, 0), new Size(20,this.Height)); //invalidate area for playhead
                Invalidate(new Rectangle(new Point(mytimecode.Bounds.X + StagePos.X, mytimecode.Bounds.Y + StagePos.Y),mytimecode.Bounds.Size)); // invalidate timecode display
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

            #region boygrouping
            //iaa special feature for boygrouping
            if (ClipXMLIn.IsChanged && ClipXMLIn[0].Length > 0)
            {
                clipList = DeserializeClipList(ClipXMLIn[0]);
                Invalidate();
            }
            #endregion

            #region updateEnumGetClip
            //inform getClip to update enums a few frames
            if (needsUpdate)
            {
                if (UpdateCounter > 1)
                {
                    needsUpdate = false;
                    UpdateCounter = 0;
                }
                UpdateCounter += 1;
            } 
            #endregion

        }
    }
}