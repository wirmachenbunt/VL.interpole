using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace customWinformsControl
{
    class CustomTextbox : TextBox
    {

        public bool isClick = false;
        public double oldMouseY = 0;

        public CustomTextbox()
        {
            this.Text = "0";
            this.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.BackColor = System.Drawing.SystemColors.ControlDarkDark;
            this.ForeColor = System.Drawing.SystemColors.ControlLight;
            //this.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Font = new System.Drawing.Font(this.Font.FontFamily, 16);
            //this.ShortcutsEnabled = false;

            this.ContextMenu = new ContextMenu();
            //this.ReadOnly = true;
            //this.GotFocus += TextBoxGotFocus;
            //this.Cursor = Cursors.Arrow; // mouse cursor like in other controls
        }

        [DllImport("user32.dll")]
        static extern bool HideCaret(IntPtr hWnd);

      

        private void TextBoxGotFocus(object sender, EventArgs args)
        {
            //HideCaret(this.Handle);
        }


        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            //if ((Control.ModifierKeys & (Keys.Alt | Keys.Shift)) == Keys.None && Control.MouseButtons == MouseButtons.None) {

            Console.WriteLine(isClick.ToString());
            if (isClick && e.Button == MouseButtons.Right)
            {
                
                double delta = e.Y - oldMouseY;
                double val = Convert.ToDouble(this.Text);
                val -= delta/1000;

                this.Text = val.ToString();
                oldMouseY = e.Y;
                
            }

            Console.WriteLine("move");
        }



        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if(e.Button == MouseButtons.Right)
            {
                oldMouseY = 0;
                isClick = true;
                Console.WriteLine(e.Y.ToString());
                this.ShortcutsEnabled = false;
            }
            
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            this.ReadOnly = false;
            Console.WriteLine("DoubleClick");
        }



        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            base.OnMouseUp(mevent);
            isClick = false;
            Console.WriteLine("ButtonReleased");
            this.ShortcutsEnabled = true;
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            Console.WriteLine("move");
        }
    }
}
