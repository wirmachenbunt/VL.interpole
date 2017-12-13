using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Threading;
using System.Timers;
using System.Drawing;
using System.Diagnostics;
using System.Runtime.InteropServices;


namespace threadedForm
{

    public class Form1 : Form
    {



        Color MyBackgroundColor = Color.FromArgb(20, 20, 20);
        Graphics g;
        bool IsRunning = true;
        int counter = 0;

        Stopwatch stopWatch = Stopwatch.StartNew();

        readonly TimeSpan TargetElapsedTime = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);
        readonly TimeSpan MaxElapsedTime = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 10);

        TimeSpan accumulatedTime;
        TimeSpan lastTime;


        public Form1()
        {
            //setup the gui


            Application.Idle += new EventHandler(Application_Idle);
            this.Show();

            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.Opaque, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

            
            InitializeComponent();
            Application.Run();

            this.DoubleBuffered = true;
            g = this.CreateGraphics();
        }


      



        public void Application_Idle(object sender, EventArgs e)
        {
            NativeMethods.Message message;

            while (!NativeMethods.PeekMessage(out message, IntPtr.Zero, 0, 0, 0))
            {

                Render();
                Thread.Sleep(50);
                //Tick(sender, e);

            }

        }

        void Tick(object sender, EventArgs e)
        {
            TimeSpan currentTime = stopWatch.Elapsed;
            TimeSpan elapsedTime = currentTime - lastTime;
            lastTime = currentTime;

            if (elapsedTime > MaxElapsedTime)
            {
                elapsedTime = MaxElapsedTime;
            }

            accumulatedTime += elapsedTime;

            bool updated = false;

            while (accumulatedTime >= TargetElapsedTime)
            {
                Update();

                accumulatedTime -= TargetElapsedTime;
                updated = true;
            }

            if (updated)
            {
                Invalidate();
            }
        }



        static class NativeMethods
        {
            [StructLayout(LayoutKind.Sequential)]
            public struct Message
            {
                public IntPtr hWnd;
                public uint Msg;
                public IntPtr wParam;
                public IntPtr lParam;
                public uint Time;
                public System.Drawing.Point Point;
            }

            [DllImport("User32.dll", CharSet = CharSet.Auto)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool PeekMessage(out Message message, IntPtr hWnd, uint filterMin, uint filterMax, uint flags);
        }



        void Render()
        {
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            
            Random r = new Random();

            g = pe.Graphics;
            counter++;
            g.Clear(MyBackgroundColor);
            Pen pn = new Pen(Color.Blue, 1000);
            Rectangle rect = new Rectangle(counter, 1000, 200, 100);
            g.DrawEllipse(pn, rect);
            if (counter > 1000) counter = 0;

        }



        private void InitializeComponent()
        {

            this.Name = "Hallooooooo";
            this.Text = "Hallooooooo";
            this.ResumeLayout(false);
            this.PerformLayout();


        }


    }
}
