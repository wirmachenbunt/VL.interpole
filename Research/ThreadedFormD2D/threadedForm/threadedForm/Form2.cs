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

        public int Hallo = 1000;

        Color MyBackgroundColor = Color.FromArgb(20, 20, 20);
        Graphics g;
        bool IsRunning = true;
        public int counter = 0;
        SolidBrush OrangeBrush = new SolidBrush(Color.Orange);
        SolidBrush brightGrayBrush = new SolidBrush(Color.FromArgb(150, 0, 150));
        Font clipFont = new Font("Segoe UI", 20, FontStyle.Regular);
        StringFormat format = new StringFormat(StringFormatFlags.LineLimit | StringFormatFlags.NoWrap, 1003);
        SolidBrush textBrush = new SolidBrush(Color.FromArgb(150, 150, 150));

        public Form1()
        {

            InitializeComponent();
            
        }

        

        void Render()
        {
            this.Invalidate();        
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            
            Random r = new Random();
            int x = Cursor.Position.X;
            g = pe.Graphics;
            
            g.Clear(MyBackgroundColor);
            
            


            for (int i = 0; i < 200; i++)
            {
                Rectangle rectt = new Rectangle(r.Next(0,2000), r.Next(0, 2000), 100, 100);
                g.FillEllipse(brightGrayBrush, rectt);

                g.DrawString("blub", clipFont, textBrush, rectt, format); //text
            }

            Rectangle rect = new Rectangle(PointToClient(Cursor.Position).X, PointToClient(Cursor.Position).Y, 200, 200);
            g.FillEllipse(OrangeBrush, rect);

            if (counter > 1000) counter = 0;
            

        }



        private void InitializeComponent()
        {
            Application.Idle += new EventHandler(Application_Idle);
            
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.Opaque, true);
            this.SetStyle(ControlStyles.ResizeRedraw, true);
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);



            this.Name = "Hallooooooo";
            this.Text = "Hallooooooo";
            this.ResumeLayout(false);
            this.PerformLayout();

            this.ControlBox = false;
            

            this.DoubleBuffered = true;
            g = this.CreateGraphics();

        }


        public void Application_Idle(object sender, EventArgs e)
        {
            NativeMethods.Message message;

            while (!NativeMethods.PeekMessage(out message, IntPtr.Zero, 0, 0, 0))
            {

                Render();
                counter++;
                Thread.Sleep(1);
                //Tick(sender, e);

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



    }


}
