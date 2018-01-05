using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ImageZoom
{
    public partial class ImageZoomMainForm : Form
    {
        Image img;
        Point mouseDown;
        int startx = 0;                         // offset of image when mouse was pressed
        int starty = 0;
        int imgx = 0;                         // current offset of image
        int imgy = 0;

        bool mousepressed = false;  // true as long as left mousebutton is pressed
        float zoom = 1;

        public ImageZoomMainForm()
        {

            InitializeComponent();
            string imagefilename = @"..\..\test.tif";
            img = Image.FromFile(imagefilename);

            Graphics g = this.CreateGraphics();

            //// Fit whole image
            //zoom = Math.Min(
            //    ((float)pictureBox.Height / (float)img.Height) * (img.VerticalResolution / g.DpiY),
            //    ((float)pictureBox.Width / (float)img.Width) * (img.HorizontalResolution / g.DpiX)
            //);

            // Fit width
            zoom = ((float)pictureBox.Width / (float)img.Width) * (img.HorizontalResolution / g.DpiX);

            pictureBox.Paint += new PaintEventHandler(imageBox_Paint);
        }

        private void pictureBox_MouseMove(object sender, EventArgs e)
        {
            MouseEventArgs mouse = e as MouseEventArgs;

            if (mouse.Button == MouseButtons.Left)
            {
                Point mousePosNow = mouse.Location;

                int deltaX = mousePosNow.X - mouseDown.X; // the distance the mouse has been moved since mouse was pressed
                int deltaY = mousePosNow.Y - mouseDown.Y;

                imgx = (int)(startx + (deltaX / zoom));  // calculate new offset of image based on the current zoom factor
                imgy = (int)(starty + (deltaY / zoom));

                pictureBox.Refresh();
            }
        }

        private void imageBox_MouseDown(object sender, EventArgs e)
        {
            MouseEventArgs mouse = e as MouseEventArgs;

            if (mouse.Button == MouseButtons.Left)
            {
                if (!mousepressed)
                {
                    mousepressed = true;
                    mouseDown = mouse.Location;
                    startx = imgx;
                    starty = imgy;
                }
            }
        }

        private void imageBox_MouseUp(object sender, EventArgs e)
        {
            mousepressed = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            float oldzoom = zoom;

            if (e.Delta > 0)
            {
                zoom += 0.1F;
            }

            else if (e.Delta < 0)
            {
                zoom = Math.Max(zoom - 0.1F, 0.01F);
            }

            MouseEventArgs mouse = e as MouseEventArgs;
            Point mousePosNow = mouse.Location;

            int x = mousePosNow.X - pictureBox.Location.X;    // Where location of the mouse in the pictureframe
            int y = mousePosNow.Y - pictureBox.Location.Y;

            Debug.WriteLine("picturebox location" + pictureBox.Location.X.ToString());
            Debug.WriteLine(pictureBox.Location.Y.ToString());

            int oldimagex = (int)(x / oldzoom);  // Where in the IMAGE is it now
            int oldimagey = (int)(y / oldzoom);

            int newimagex = (int)(x / zoom);     // Where in the IMAGE will it be when the new zoom i made
            int newimagey = (int)(y / zoom);

            imgx = newimagex - oldimagex + imgx;  // Where to move image to keep focus on one point
            imgy = newimagey - oldimagey + imgy;

            pictureBox.Refresh();  // calls imageBox_Paint
        }

        private void imageBox_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.ScaleTransform(zoom, zoom);
            e.Graphics.DrawImage(img, imgx, imgy);
        }


        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            const int WM_KEYDOWN = 0x100;
            const int WM_SYSKEYDOWN = 0x104;

            if ((msg.Msg == WM_KEYDOWN) || (msg.Msg == WM_SYSKEYDOWN))
            {
                switch (keyData)
                {
                    case Keys.Right:
                        imgx -= (int)(pictureBox.Width * 0.1F / zoom);
                        pictureBox.Refresh();
                        break;

                    case Keys.Left:
                        imgx += (int)(pictureBox.Width * 0.1F / zoom);
                        pictureBox.Refresh();
                        break;

                    case Keys.Down:
                        imgy -= (int)(pictureBox.Height * 0.1F / zoom);
                        pictureBox.Refresh();
                        break;

                    case Keys.Up:
                        imgy += (int)(pictureBox.Height * 0.1F / zoom);
                        pictureBox.Refresh();
                        break;

                    case Keys.PageDown:
                        imgy -= (int)(pictureBox.Height * 0.90F / zoom);
                        pictureBox.Refresh();
                        break;

                    case Keys.PageUp:
                        imgy += (int)(pictureBox.Height * 0.90F / zoom);
                        pictureBox.Refresh();
                        break;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}