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
    [PluginInfo(Name = "Clips",
                Category = "GUI",
                Help = "Clip Timeline",
                Tags = "c#",
                AutoEvaluate = true)]
    #endregion PluginInfo
    public partial class Clips : UserControl, IPluginEvaluate
    {
        #region fields & pins

        [Import()]
        public ILogger FLogger;

    

        //ClipsDrawing p = new ClipsDrawing(); // create AutomataPaint Object

        #endregion fields & pins

        #region constructor and init

        public Clips()
        {
            //setup the gui
            InitializeComponent();
        }

        void InitializeComponent()
        {
            //clear controls in case init is called multiple times
            Controls.Clear();

            Paint += PaintAutomata; //paint event
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
           // FLogger.Log(LogType.Debug,Convert.ToString(deltaX));
            //Invalidate();
        }
    }
}