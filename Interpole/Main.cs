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

        [Config("ClipXML")]
        public IDiffSpread<string> ClipXML;

        [Output("Clips")]
        public ISpread<Interpole> ClipsOut;

        [Import()]
        public ILogger FLogger;

        bool initialized = false; // controlling init of plugin
        bool play = false;
        public bool needsUpdate = true;

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
            if(!initialized)
            {
                try
                {
                    clipList = DeserializeClipList(ClipXML[0]);
                    FLogger.Log(LogType.Debug, "Interpole successful initialized");
                    ApplyTimeZoom();
                    Invalidate();
                }
                catch { FLogger.Log(LogType.Debug, "Loading clip list failed :("); }

                initialized = true;
            }
        }

        public void Play()
        {
            if (play)
            {
                playPosFrames += 1;
                Invalidate();
            }
            
        }


        //called when data for any output pin is requested
        public void Evaluate(int SpreadMax)
        {
            InitInterpole();
            Play();

            //FLogger.Log(LogType.Debug, Convert.ToString(this.Width));

            ClipsOut.SliceCount = 1; // set output for additional nodes
            ClipsOut[0] = this;

            if(needsUpdate) FLogger.Log(LogType.Debug, "update");
            //needsUpdate = false;
        }
    }
}