using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace customWinformsControl 
{
    class CustomLabel :Label
    {
        public bool isClick = false;
        public double oldMouseY = 0;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            //if ((Control.ModifierKeys & (Keys.Alt | Keys.Shift)) == Keys.None && Control.MouseButtons == MouseButtons.None) {

            Console.WriteLine(isClick.ToString());
            if (isClick)
            {
                double delta = e.Y - oldMouseY;
                Console.WriteLine(delta.ToString());
                double val = Convert.ToDouble(this.Text);
                val += delta;
               
                this.Text = val.ToString();
                oldMouseY = e.Y;
            }

            Console.WriteLine("move");
        }



        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);


            oldMouseY = 0;
            isClick = true;
            Console.WriteLine(e.Y.ToString());
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            

            if (this != null)
            {
                var editor = new LabelEditor();

                //editor.Location = this.Parent.Location;

                editor.Location = new Point(this.Location.X + this.Parent.Location.X, this.Location.Y + this.Parent.Location.Y);
                editor.Text = this.Text;

                if (DialogResult.OK == editor.ShowDialog())
                {
                    this.Text = editor.Text;
                }
            }


            Console.WriteLine("DoubleClick");
        }



        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            base.OnMouseUp(mevent);
            isClick = false;
            Console.WriteLine("ButtonReleased");
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);

            Console.WriteLine("move");


        }



    }
}
