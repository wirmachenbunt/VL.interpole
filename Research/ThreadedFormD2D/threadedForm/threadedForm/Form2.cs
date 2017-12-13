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
using SlimDX;
using D2D = SlimDX.Direct2D;

namespace threadedForm
{
    
    public class Form1:Form
    {
       
        Graphics g;
        bool IsRunning = true;
        

        public Form1()
        {
            //setup the gui
           
           
                Application.Idle += new EventHandler(Application_Idle);
                this.Show();
                InitializeComponent();
                Application.Run();
           
            
        }

        public void Application_Idle(object sender, EventArgs e)
        {
            
             Render();
            
                
        }


        void Render()
        {
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
           
                Random r = new Random();

                g = pe.Graphics;
                Pen pn = new Pen(Color.Blue, 1000);
                Rectangle rect = new Rectangle(r.Next(5, 100), r.Next(5, 1000), 200, 100);
                g.DrawEllipse(pn, rect);
            
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
