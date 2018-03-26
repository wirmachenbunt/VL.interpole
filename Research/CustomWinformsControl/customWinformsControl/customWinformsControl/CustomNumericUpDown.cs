using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace customWinformsControl
{
    class CustomNumericUpDown : NumericUpDown
    {
        public bool isClick = false;
        public double oldMouseY = 0;
        

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

          

            isClick = true;
            Console.WriteLine(e.Y.ToString());
            //this.Value = 10;

           
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


        protected override void OnMouseMove(MouseEventArgs e)
        {
            Console.WriteLine(isClick.ToString());
            if (isClick)
            {
                double delta = e.Y - oldMouseY;
                Console.WriteLine(delta.ToString());

                this.Value += Convert.ToDecimal(delta);
                oldMouseY = e.Y;
            }

            base.OnMouseMove(e);
            //if ((Control.ModifierKeys & (Keys.Alt | Keys.Shift)) == Keys.None && Control.MouseButtons == MouseButtons.None) {

            Console.WriteLine("move");



        }




    }
}
