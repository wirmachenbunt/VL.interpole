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
using VVVV.Utils.VMath;
using System.Diagnostics;
using System.IO;
using System.ComponentModel;

namespace Interpole
{
    partial class InterpoleBase : Form
    {
        #region variables

        int TargetStagePosY = 0; //for moving stage down to KF Clip
        Rectangle KFinspectorBounds;


        #endregion



        public void DrawKFWorkspace(PaintEventArgs e)
        {
            DrawKFClip(e);
        }

        private void DrawKFinspector(PaintEventArgs e)
        {
            KFinspectorBounds = new Rectangle(0, 0, 200, 200);
        }

        public void DrawKFClip(PaintEventArgs e)
        {
            Color ClipColor = Color.FromName(KeyframeEditorClip.Color);
            SolidBrush ClipBrush = new SolidBrush(ClipColor);

            e.Graphics.FillRectangle(ClipBrush, KeyframeEditorClip.Bounds);

            string text = KeyframeEditorClip.Name; // create text

            stringSize = e.Graphics.MeasureString(text, clipFont, 400);

            Point center = new Point(Convert.ToInt32(KeyframeEditorClip.Bounds.Location.X * Zoom), KeyframeEditorClip.Bounds.Y - Convert.ToInt16(stringSize.Height));

            Rectangle Bounds = new Rectangle(
            center,
            new Size(Convert.ToInt32(stringSize.Width + 1),
            Convert.ToInt32(stringSize.Height + 1)));

            e.Graphics.ScaleTransform(1 / Zoom, 1); //unscale stage to display proper text
            e.Graphics.DrawString(text, clipFont, textBrush, Bounds, format); //text
            e.Graphics.ScaleTransform(Zoom, 1); //rescale stage
        }

        void DrawInsideRegion()
        {
            /*
            Rectangle myregionRect = new Rectangle(0, 0, 500, 500);
            pe.Graphics.DrawRectangle(GrayPen, myregionRect);

            Region viewRegion = new Region(myregionRect);
            Matrix transformMatrix = new Matrix();
            transformMatrix.Translate(50, 70);

            viewRegion.Transform(transformMatrix);

            pe.Graphics.SetClip(viewRegion, CombineMode.Replace);
            pe.Graphics.FillRectangle(OrangeBrush, new Rectangle(0, 0, 100, 100));
            pe.Graphics.ResetClip();
            */


        }


    }
}
