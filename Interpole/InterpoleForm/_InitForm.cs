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
using System.Diagnostics;

namespace Interpole
{
    partial class InterpoleBase : Form
    {
        #region variables

        //Interpole Object keeping all data like clips and window settings
        public InterpoleData ipD;
        public string InterpoleDataSerialized;


        //Event in case something changed
        public event ChangedHandler Changed;
        public delegate void ChangedHandler(InterpoleBase interpoleForm, EventArgs e);

        //public List<Clip> clipList; //master clip list
        public List<Clip> clipBoard = new List<Clip>(); // copy paste clips
        public List<int> snapPosList = new List<int>(); // snap position list
        public Clip dragDurationClip = new Clip();

        Stack<string> undoXML = new Stack<string>();
        
        public bool play = false;
        public string rememberLastFunction = "idle";  // keep track of last command

        public string InterpoleXML = "";

        #endregion

        public InterpoleBase()
        {
            InitObjects(); // init interpole data object with all infos
            InitializeComponent();

            //Register Resize Form Event
            this.ResizeEnd += Window_Resize;

            //Register Update Event to export all Settings
            this.Changed += UpdateInterpoleString;

        }

        public void InitObjects()
        {
            ipD = new InterpoleData();
            ipD.ClipList = new List<Clip>();
            ipD.Loop = false;
            ipD.MarkerList = new List<Marker>();
            ipD.WLocation = new Point(10, 10); //window location
            ipD.WSize = new Size(600, 400); // window size
            ipD.UILock = false;
            ipD.PlayHead = 0;
            ipD.FrameRate = 60;
        }

        private void InitializeComponent()
        {
            Application.Idle += new EventHandler(Application_Idle);

            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.Opaque, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

            this.ShowIcon = false;

            this.Name = "Interpole";
            this.Text = "Interpole";
            this.ResumeLayout(false);
            this.PerformLayout();

            this.ControlBox = false;

            this.DoubleBuffered = true;
            g = this.CreateGraphics();

            InitMenuToolStrip();

            //bind events
            MouseMove += Form1_MouseMove; //mouse move event
            MouseDoubleClick += Form1_MouseDoubleClick; //mouse click event
            MouseDown += Form1_MouseDown; //mouse down event
            MouseUp += Form1_MouseUp;
            MouseWheel += Form1_MouseWheel;
            KeyDown += Form_Keydown;

            this.Location = ipD.WLocation;
            this.Size = ipD.WSize;
        }

        void UpdateInterpoleString(object sender, EventArgs e)
        {
            InterpoleDataSerialized = SerializeInterpoleData(ipD);
        }

        void Window_Resize(object sender, EventArgs e)
        {
            ipD.WLocation = this.Location;
            ipD.WSize = this.Size;
            Changed(this, e); // trigger changed interpole event
        }
    }
}
