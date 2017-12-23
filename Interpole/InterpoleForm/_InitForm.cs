using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using VVVV.Core.Logging;
using VVVV.PluginInterfaces.V2;
using System.ComponentModel.Composition;
using System.Runtime.InteropServices;
using System.Threading;

namespace Interpole
{
    partial class InterpoleForm : Form
    {
        #region variables

        private InterpoleData ipD;

        //public List<Clip> clipList; //master clip list
        public List<Clip> clipBoard = new List<Clip>(); // copy paste clips
        public List<int> snapPosList = new List<int>(); // snap position list
        public Clip dragDurationClip = new Clip();

        Stack<string> undoXML = new Stack<string>();

        public int playPosFrames = 100;
        public string rememberLastFunction = "idle";  // keep track of last command

        bool play = false;

        public string InterpoleXML = "";

        #endregion

        public InterpoleForm()
        {
            InitObjects(); // init interpole data object with all infos
            InitializeComponent();
            
        }

        public void InitObjects()
        {
            //clipList = new List<Clip>();

            ipD = new InterpoleData();

            ipD.ClipList = new List<Clip>();
            ipD.Loop = false;
            ipD.MarkerList = new List<Marker>();
            ipD.WLocation = new Point(10, 10); //window location
            ipD.WSize = new Size(600, 400); // window size
            ipD.UILock = false;
        }


    }
}
