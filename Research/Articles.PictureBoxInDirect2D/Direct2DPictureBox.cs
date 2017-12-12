using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SlimDX;

//Some namespace mappings
using GDI = System.Drawing;
using D2D = SlimDX.Direct2D;
using DXGI = SlimDX.DXGI;

namespace Testing.FrequentDirect2DDRawing
{
    public partial class Direct2DPictureBox : Control
    {
        private PictureBoxSizeMode m_sizeMode;
        private bool m_direct2DBitmapInvalid;
        private GDI.Brush m_backgroundBrush;
        private GDI.Bitmap m_gdiBitmap;
        private D2D.Bitmap m_direct2DBitmap;
        private D2D.Factory m_factory;
        private D2D.WindowRenderTarget m_renderTarget;

        /// <summary>
        /// Initializes a new instance of the <see cref="MainWindow"/> class.
        /// </summary>
        public Direct2DPictureBox()
        {
            m_sizeMode = PictureBoxSizeMode.CenterImage;

            //Mark Direct2D bitmap as invalid
            m_direct2DBitmapInvalid = true;

            //Create background-brush
            m_backgroundBrush = new SolidBrush(this.BackColor);

            //Set styles
            base.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            base.SetStyle(ControlStyles.ResizeRedraw, true);
            base.SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
            base.SetStyle(ControlStyles.Opaque, true);
            base.DoubleBuffered = false;
        }

        /// <summary>
        /// Gets an image rectangle based on the given size mode.
        /// </summary>
        private Rectangle GetDrawingRectangle(PictureBoxSizeMode sizeMode, Size imageSize)
        {
            Rectangle result = Rectangle.Empty;

            switch (sizeMode)
            {
                case PictureBoxSizeMode.StretchImage:
                case PictureBoxSizeMode.AutoSize:
                    result = new Rectangle(0, 0, this.Width, this.Height);
                    break;

                case PictureBoxSizeMode.CenterImage:
                    result = new Rectangle(
                        (int)(this.Width / 2f - imageSize.Width / 2f),
                        (int)(this.Height / 2f - imageSize.Height / 2f),
                        imageSize.Width, imageSize.Height);
                    break;

                case PictureBoxSizeMode.Normal:
                    result = new Rectangle(0, 0, imageSize.Width, imageSize.Height);
                    break;

                case PictureBoxSizeMode.Zoom:
                    double scaleFactorX = (double)imageSize.Width / (double)this.Width;
                    double scaleFactorY = (double)imageSize.Height / (double)this.Height;
                    double factorToUse = scaleFactorX > scaleFactorY ? scaleFactorX : scaleFactorY;
                    Size newSize = new Size(
                        (int)(imageSize.Width / factorToUse),
                        (int)(imageSize.Height / factorToUse));
                    result = new Rectangle(
                        (int)(this.Width / 2f - newSize.Width / 2f),
                        (int)(this.Height / 2f - newSize.Height / 2f),
                        newSize.Width, newSize.Height);
                    break;
            }

            return result;
        }

        /// <summary>
        /// Load a Direct2D bitmap from the given gdi resource.
        /// </summary>
        private D2D.Bitmap LoadBitmap(Bitmap drawingBitmap)
        {
            D2D.Bitmap result = null;

            //Lock the gdi resource
            BitmapData drawingBitmapData = drawingBitmap.LockBits(
                new Rectangle(0, 0, drawingBitmap.Width, drawingBitmap.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);

            //Prepare loading the image from gdi resource
            DataStream dataStream = new DataStream(
                drawingBitmapData.Scan0,
                drawingBitmapData.Stride * drawingBitmapData.Height,
                true, false);
            D2D.BitmapProperties properties = new D2D.BitmapProperties();
            properties.PixelFormat = new D2D.PixelFormat(
                DXGI.Format.B8G8R8A8_UNorm,
                D2D.AlphaMode.Premultiplied);

            //Load the image from the gdi resource
            result = new D2D.Bitmap(
                m_renderTarget,
                new Size(drawingBitmap.Width, drawingBitmap.Height),
                dataStream, drawingBitmapData.Stride,
                properties);

            //Unlock the gdi resource
            drawingBitmap.UnlockBits(drawingBitmapData);

            return result;
        }

        /// <summary>
        /// Loads all contents of the gdi bitmap into the target direct2d bitmap.
        /// </summary>
        private void SetBitmapContents(D2D.Bitmap targetBitmap, Bitmap drawingBitmap)
        {
            //Lock the gdi resource
            BitmapData drawingBitmapData = drawingBitmap.LockBits(
                new Rectangle(0, 0, drawingBitmap.Width, drawingBitmap.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            targetBitmap.FromMemory(drawingBitmapData.Scan0, drawingBitmapData.Stride);
            drawingBitmap.UnlockBits(drawingBitmapData);
        }

        /// <summary>
        /// Disposes the given object and returns null.
        /// </summary>
        private static T DisposeObject<T>(T objectToDispose)
            where T : class, IDisposable
        {
            if (objectToDispose == null) { return null; }

            try { objectToDispose.Dispose(); }
            catch (Exception) { }
            return null;
        }

        /// <summary>
        /// Called when the control's handle is created.
        /// </summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            try
            {
                //Create the render target
                m_factory = new D2D.Factory(
                    D2D.FactoryType.SingleThreaded,
                    D2D.DebugLevel.Information);
                m_renderTarget = new D2D.WindowRenderTarget(
                    m_factory,
                    new D2D.WindowRenderTargetProperties()
                    {
                        Handle = this.Handle,
                        PixelSize = this.Size,
                        PresentOptions = D2D.PresentOptions.Immediately
                    });
            }
            catch (Exception)
            {
                if (m_renderTarget != null) { m_renderTarget.Dispose(); }
                if (m_factory != null) { m_factory.Dispose(); }
                m_renderTarget = null;
                m_factory = null;
            }
        }

        /// <summary>
        /// Called when the control's size has changed.
        /// </summary>
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);

            if (m_factory != null)
            {
                m_renderTarget.Resize(new Size(this.Width, this.Height));
            }
        }

        /// <summary>
        /// Called when the control's handle is destroyed.
        /// </summary>
        protected override void OnHandleDestroyed(EventArgs e)
        {
            base.OnHandleDestroyed(e);

            m_renderTarget = DisposeObject(m_renderTarget);
            m_factory = DisposeObject(m_factory);
        }

        /// <summary>
        /// Called when background color has changed.
        /// </summary>
        protected override void OnBackColorChanged(EventArgs e)
        {
            base.OnBackColorChanged(e);

            m_backgroundBrush = DisposeObject(m_backgroundBrush);
            m_backgroundBrush = new SolidBrush(this.BackColor);
        }

        /// <summary>
        /// Called when control has to paint itself.
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            if (m_renderTarget != null)
            {
                //Load Direct2D bitmap if it is invalid
                if (m_direct2DBitmapInvalid && (m_gdiBitmap != null))
                {
                    if ((m_direct2DBitmap != null) &&
                        (m_direct2DBitmap.PixelSize.Width == m_gdiBitmap.Width) &&
                        (m_direct2DBitmap.PixelSize.Height == m_gdiBitmap.Height))
                    {
                        SetBitmapContents(m_direct2DBitmap, m_gdiBitmap);
                    }
                    else
                    {
                        m_direct2DBitmap = DisposeObject(m_direct2DBitmap);
                        m_direct2DBitmap = LoadBitmap(m_gdiBitmap);
                    }
                }
                else if (m_gdiBitmap == null)
                {
                    m_direct2DBitmap = DisposeObject(m_direct2DBitmap);
                }
                m_direct2DBitmapInvalid = false;

                //Direct2D is initialized, so draw using Direct2D
                m_renderTarget.BeginDraw();
                try
                {
                    m_renderTarget.Clear(new Color4(this.BackColor));

                    if (m_direct2DBitmap != null)
                    {
                        m_renderTarget.DrawBitmap(
                            m_direct2DBitmap,
                            GetDrawingRectangle(m_sizeMode, m_direct2DBitmap.PixelSize));
                    }
                }
                finally
                {
                    m_renderTarget.EndDraw();
                }
            }
            else
            {
                //Perform fallback rendering using Gdi
                if (m_gdiBitmap != null)
                {
                    e.Graphics.DrawImage(m_gdiBitmap, GetDrawingRectangle(m_sizeMode, m_gdiBitmap.Size));
                }
                else
                {
                    e.Graphics.FillRectangle(m_backgroundBrush, new Rectangle(0, 0, this.Width, this.Height));
                }
            }
        }

        /// <summary>
        /// Gets or sets the currently displayed bitmap.
        /// </summary>
        [Category("Rendering")]
        public GDI.Bitmap Image
        {
            get { return m_gdiBitmap; }
            set
            {
                if (m_gdiBitmap != value)
                {
                    m_gdiBitmap = value;
                    m_direct2DBitmapInvalid = true;
                    this.Invalidate();
                }
            }
        }

        /// <summary>
        /// Gets or sets the size mode.
        /// </summary>
        [Category("Rendering")]
        public PictureBoxSizeMode SizeMode
        {
            get { return m_sizeMode; }
            set
            {
                if (m_sizeMode != value)
                {
                    m_sizeMode = value;
                    this.Invalidate();
                }
            }
        }
    }
}
