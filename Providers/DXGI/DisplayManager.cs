using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;

namespace DXGI_DesktopDuplication
{
    public class DisplayManager
    {
        private const int NUMVERTICES = 6;
        private readonly Device device;

        public DisplayManager(Device device)
        {
            this.device = device;
        }


        public void ProcessFrame(FrameData data, ref Texture2D sharedSurface, OutputDescription desktopDescription)
        {
            if (data.FrameInfo.TotalMetadataBufferSize > 0)
            {
                Texture2DDescription description = data.Frame.Description;

                if (data.MoveCount > 0)
                {
                    CopyMoveRects(ref sharedSurface, data.MoveRectangles, data.MoveCount, desktopDescription,
                        description.Width, description.Height);
                }

                if (data.DirtyCount > 0)
                {
                    CopyDirtyRects(data.Frame, ref sharedSurface, data.DirtyRectangles, data.DirtyCount,
                        desktopDescription);
                }
            }
        }

        //set appropriate source and destination rects for move rects
        private void SetMoveRect(ref Rectangle srcRect, ref Rectangle destRect, OutputDescription desktopDescription,
            OutputDuplicateMoveRectangle moveRect, int texWidth, int texHeight)
        {
            switch (desktopDescription.Rotation)
            {
                case DisplayModeRotation.Unspecified:
                case DisplayModeRotation.Identity:
                {
                    srcRect.Left = moveRect.SourcePoint.X;
                    srcRect.Top = moveRect.SourcePoint.Y;
                    srcRect.Right = moveRect.SourcePoint.X + moveRect.DestinationRect.Right -
                                    moveRect.DestinationRect.Left;
                    srcRect.Bottom = moveRect.SourcePoint.Y + moveRect.DestinationRect.Bottom -
                                     moveRect.DestinationRect.Top;

                    destRect = moveRect.DestinationRect;
                    break;
                }
                case DisplayModeRotation.Rotate90:
                {
                    srcRect.Left =
                        texHeight -
                        (moveRect.SourcePoint.Y + moveRect.DestinationRect.Bottom - moveRect.DestinationRect.Top);
                    srcRect.Top = moveRect.SourcePoint.X;
                    srcRect.Right = texHeight - moveRect.SourcePoint.Y;
                    srcRect.Bottom =
                        moveRect.SourcePoint.X + moveRect.DestinationRect.Right - moveRect.DestinationRect.Left;

                    destRect.Left = texHeight - moveRect.DestinationRect.Bottom;
                    destRect.Top = moveRect.DestinationRect.Left;
                    destRect.Right = texHeight - moveRect.DestinationRect.Top;
                    destRect.Bottom = moveRect.DestinationRect.Right;

                    break;
                }
                case DisplayModeRotation.Rotate180:
                {
                    srcRect.Left = texHeight -
                                   (moveRect.SourcePoint.X + moveRect.DestinationRect.Right -
                                    moveRect.DestinationRect.Left);
                    srcRect.Top = texHeight -
                                  (moveRect.SourcePoint.Y + moveRect.DestinationRect.Bottom -
                                   moveRect.DestinationRect.Top);
                    srcRect.Right = texWidth - moveRect.SourcePoint.X;
                    srcRect.Bottom = texHeight - moveRect.SourcePoint.Y;

                    destRect.Left = texWidth - moveRect.DestinationRect.Right;
                    destRect.Top = texHeight - moveRect.DestinationRect.Bottom;
                    destRect.Right = texWidth - moveRect.DestinationRect.Left;
                    destRect.Bottom = texHeight - moveRect.DestinationRect.Top;
                    break;
                }
                case DisplayModeRotation.Rotate270:
                {
                    srcRect.Left = moveRect.SourcePoint.X;
                    srcRect.Top = texWidth -
                                  (moveRect.SourcePoint.X + moveRect.DestinationRect.Right -
                                   moveRect.DestinationRect.Left);
                    srcRect.Right = moveRect.SourcePoint.Y + moveRect.DestinationRect.Bottom -
                                    moveRect.DestinationRect.Top;
                    srcRect.Bottom = texWidth - moveRect.SourcePoint.X;

                    destRect.Left = moveRect.DestinationRect.Top;
                    destRect.Top = texWidth - moveRect.DestinationRect.Right;
                    destRect.Right = moveRect.DestinationRect.Bottom;
                    destRect.Bottom = texWidth - moveRect.DestinationRect.Left;
                    break;
                }
                default:
                {
                    destRect = new Rectangle();
                    srcRect = new Rectangle();
                    break;
                }
            }
        }

        private void CopyMoveRects(ref Texture2D sharedSurface, OutputDuplicateMoveRectangle[] moveRectangles,
            int moveCount, OutputDescription desktopDescription,
            int texWidth, int texHieght)
        {
            Texture2DDescription fullDescription = sharedSurface.Description;

            //make new intermediate surface to copy into for moving
            Texture2DDescription moveDescription = fullDescription;
            moveDescription.Width = desktopDescription.DesktopBounds.Right - desktopDescription.DesktopBounds.Left;
            moveDescription.Height = desktopDescription.DesktopBounds.Bottom - desktopDescription.DesktopBounds.Top;
            moveDescription.BindFlags = BindFlags.RenderTarget;

            var moveSurface = new Texture2D(device, moveDescription);

            for (int i = 0; i < moveCount; i++)
            {
                var srcRect = new Rectangle();
                var destRect = new Rectangle();

                SetMoveRect(ref srcRect, ref destRect, desktopDescription, moveRectangles[i], texWidth, texHieght);

                //copy rect out of shared surface
                var box = new ResourceRegion(srcRect.Left + desktopDescription.DesktopBounds.Left,
                    srcRect.Top + desktopDescription.DesktopBounds.Top,
                    0, srcRect.Right + desktopDescription.DesktopBounds.Left,
                    srcRect.Bottom + desktopDescription.DesktopBounds.Top, 1);

                //TODO 不确定
                device.ImmediateContext.CopySubresourceRegion(sharedSurface, 0, box, moveSurface, 0,
                    srcRect.Left, srcRect.Top, 0);

                //copy back to shared surface
                box.Left = srcRect.Left;
                box.Top = srcRect.Top;
                box.Front = 0;
                box.Right = srcRect.Right;
                box.Bottom = srcRect.Bottom;
                box.Back = 1;
                //TODO 不确定
                device.ImmediateContext.CopySubresourceRegion(moveSurface, 0, box, sharedSurface, 0,
                    destRect.Left + desktopDescription.DesktopBounds.Left,
                    destRect.Top + desktopDescription.DesktopBounds.Top, 0);
            }
        }


        //sets up vertices for dirty rects for rotated desktops
        private void SetDirtyVert(ref Vertex[] vertices, int startIndex, Rectangle dirty, OutputDescription deskDesc,
            Texture2DDescription fullDesc, Texture2DDescription thisDesc)
        {
            //todo
            int centerX = fullDesc.Width/2;
            int centerY = fullDesc.Height/2;

            int width = deskDesc.DesktopBounds.Right - deskDesc.DesktopBounds.Left;
            int height = deskDesc.DesktopBounds.Bottom - deskDesc.DesktopBounds.Top;

            //rotation compensated destination rect
            Rectangle destDirty = dirty;

            //set appropricate coordinates compensated for rotation
            switch (deskDesc.Rotation)
            {
                case DisplayModeRotation.Rotate90:
                {
                    destDirty.Left = width - dirty.Bottom;
                    destDirty.Top = dirty.Left;
                    destDirty.Right = width - dirty.Top;
                    destDirty.Bottom = dirty.Bottom;

                    vertices[startIndex + 0].SetTexCoord(dirty.Right/(float) thisDesc.Width,
                        dirty.Bottom/(float) thisDesc.Height);
                    vertices[startIndex + 1].SetTexCoord(dirty.Left/(float) thisDesc.Width,
                        dirty.Bottom/(float) thisDesc.Height);
                    vertices[startIndex + 2].SetTexCoord(dirty.Right/(float) thisDesc.Width,
                        dirty.Top/(float) thisDesc.Height);
                    vertices[startIndex + 5].SetTexCoord(dirty.Left/(float) thisDesc.Width,
                        dirty.Top/(float) thisDesc.Height);
                    break;
                }
                case DisplayModeRotation.Rotate180:
                {
                    destDirty.Left = width - dirty.Right;
                    destDirty.Top = height - dirty.Bottom;
                    destDirty.Right = width - dirty.Left;
                    destDirty.Bottom = height - dirty.Top;

                    vertices[startIndex + 0].SetTexCoord(dirty.Right/(float) thisDesc.Width,
                        dirty.Top/(float) thisDesc.Height);
                    vertices[startIndex + 1].SetTexCoord(dirty.Right/(float) thisDesc.Width,
                        dirty.Bottom/(float) thisDesc.Height);
                    vertices[startIndex + 2].SetTexCoord(dirty.Left/(float) thisDesc.Width,
                        dirty.Top/(float) thisDesc.Height);
                    vertices[startIndex + 5].SetTexCoord(dirty.Left/(float) thisDesc.Width,
                        dirty.Bottom/(float) thisDesc.Height);
                    break;
                }
                case DisplayModeRotation.Rotate270:
                {
                    destDirty.Left = dirty.Top;
                    destDirty.Top = height - dirty.Right;
                    destDirty.Right = dirty.Bottom;
                    destDirty.Bottom = height - dirty.Left;

                    vertices[startIndex + 0].SetTexCoord(dirty.Left/(float) thisDesc.Width,
                        dirty.Top/(float) thisDesc.Height);
                    vertices[startIndex + 1].SetTexCoord(dirty.Right/(float) thisDesc.Width,
                        dirty.Top/(float) thisDesc.Height);
                    vertices[startIndex + 2].SetTexCoord(dirty.Left/(float) thisDesc.Width,
                        dirty.Bottom/(float) thisDesc.Height);
                    vertices[startIndex + 5].SetTexCoord(dirty.Right/(float) thisDesc.Width,
                        dirty.Bottom/(float) thisDesc.Height);
                    break;
                }
                default:
                {
                    vertices[startIndex + 0].SetTexCoord(dirty.Left/(float) thisDesc.Width,
                        dirty.Bottom/(float) thisDesc.Height);
                    vertices[startIndex + 1].SetTexCoord(dirty.Left/(float) thisDesc.Width,
                        dirty.Top/(float) thisDesc.Height);
                    vertices[startIndex + 2].SetTexCoord(dirty.Right/(float) thisDesc.Width,
                        dirty.Bottom/(float) thisDesc.Height);
                    vertices[startIndex + 5].SetTexCoord(dirty.Right/(float) thisDesc.Width,
                        dirty.Top/(float) thisDesc.Height);
                    break;
                }
            }

            //set positions
            vertices[startIndex + 0].SetPos((destDirty.Left + deskDesc.DesktopBounds.Left - centerX)/(float) centerX,
                -1*(destDirty.Bottom + deskDesc.DesktopBounds.Top - centerY)/(float) centerY, 0f);
            vertices[startIndex + 1].SetPos((destDirty.Left + deskDesc.DesktopBounds.Left - centerX),
                -1*(destDirty.Top + deskDesc.DesktopBounds.Top - centerY)/(float) centerY, 0f);
            vertices[startIndex + 2].SetPos((destDirty.Right + deskDesc.DesktopBounds.Left - centerX)/(float) centerX,
                -1*(destDirty.Bottom + deskDesc.DesktopBounds.Top - centerY)/(float) centerY, 0f);
            vertices[startIndex + 3].SetPos(vertices[startIndex + 2].Pos_X, vertices[startIndex + 2].Pos_Y, 0f);
            vertices[startIndex + 4].SetPos(vertices[startIndex + 1].Pos_X, vertices[startIndex + 1].Pos_Y, 0f);
            vertices[startIndex + 5].SetPos((destDirty.Right + deskDesc.DesktopBounds.Left - centerX)/(float) centerX,
                -1*(destDirty.Top + deskDesc.DesktopBounds.Top - centerY)/(float) centerY, 0f);

            vertices[startIndex + 3].SetTexCoord(vertices[startIndex + 2].TexCoord_X,
                vertices[startIndex + 2].TexCoord_Y);
            vertices[startIndex + 4].SetTexCoord(vertices[startIndex + 1].TexCoord_X,
                vertices[startIndex + 1].TexCoord_Y);
        }


        private void CopyDirtyRects(Texture2D sourceSurface, ref Texture2D sharedSurface, Rectangle[] dirtyRectangles,
            int dirtyCount, OutputDescription desktopDescription)
        {
            Texture2DDescription fullDesc = sharedSurface.Description;
            Texture2DDescription thisDesc = sourceSurface.Description;

            var renderTargetView = new RenderTargetView(device, sharedSurface);

            var shaderDesc = new ShaderResourceViewDescription();
            shaderDesc.Format = thisDesc.Format;
            shaderDesc.Dimension = ShaderResourceViewDimension.Texture2D;
            shaderDesc.Texture2D.MostDetailedMip = thisDesc.MipLevels - 1;
            shaderDesc.Texture2D.MipLevels = thisDesc.MipLevels;

            //create new shader resource view
            ShaderResourceView shaderResource = null;
            shaderResource = new ShaderResourceView(device, sourceSurface, shaderDesc);

            float[] blendFactor = {0f, 0f, 0f, 0f};
            //TODO 无对应函数

            int bytesNeeded = Marshal.SizeOf(typeof (Vertex))*NUMVERTICES*dirtyCount;

            var dirtyVertex = new Vertex[NUMVERTICES*dirtyCount];
            //TODO 不确定
            for (int i = 0; i < dirtyCount; ++i)
            {
                SetDirtyVert(ref dirtyVertex, i*NUMVERTICES, dirtyRectangles[i], desktopDescription, fullDesc, thisDesc);
            }

            //create vertex buffer
            var bufferDesc = new BufferDescription();
            bufferDesc.Usage = ResourceUsage.Default;
            bufferDesc.SizeInBytes = bytesNeeded;
            bufferDesc.BindFlags = BindFlags.VertexBuffer;
            bufferDesc.CpuAccessFlags = 0;


            var vertBuf = new Buffer(device, bufferDesc); //TODO 将dirtyVertex作为数据传入

            //TODO
        }
    }
}