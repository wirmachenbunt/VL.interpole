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

        [Output("Clips")]
        public ISpread<Interpole> ClipsOut;


        [Import()]
        public ILogger FLogger;

        //ClipsDrawing p = new ClipsDrawing(); // create AutomataPaint Object

        #endregion fields & pins

        #region constructor and init

        public Interpole()
        {
            //setup the gui
            InitializeComponent();
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


        }


        #endregion constructor and init

        //called when data for any output pin is requested
        public void Evaluate(int SpreadMax)
        {
            
           
            
        

            //FLogger.Log(LogType.Debug, Convert.ToString(this.Width));

            ClipsOut.SliceCount = 1; // set output for additional nodes
            ClipsOut[0] = this;
        }
    }
}