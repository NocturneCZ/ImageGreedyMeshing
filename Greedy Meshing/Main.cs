﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows.Forms;

namespace Greedy_Meshing
{
    public partial class Main : Form
    {
        private Color color = Color.Black;
        private Bitmap origImage;
        private Bitmap meshedImage;
        private Bitmap customSampler;
        private bool IsMeshing = false;
        private GreedyMesher mesher;
        private IEnumerator<Rectangle> mesherEnumerator;
        private Random rng = new Random();
        private Graphics gfx;
        private string Title;
        public Main()
        {
            InitializeComponent();
            GreedyMeshUpdateTimer.Interval = (int)MeshingUpdateRateSelector.Value;
            Title = this.Text;
        }

        public void StopGreedyMeshing()
        {
            IsMeshing = false;
            GreedyMeshUpdateTimer.Stop();
            SetStatus();
        }

        private void OnLoadImage(object sender, EventArgs e)
        {
            if (SelectImageDialog.ShowDialog() == DialogResult.OK) {
                SetStatus("Loading Source Image...");
                origImage?.Dispose();
                origImage = null;
                origImage = (Bitmap)Image.FromFile(SelectImageDialog.FileName);
                OriginalImageDisplay.Image = origImage;
                SetStatus();
            }
        }

        private void OnLoadCustomSampler(object sender, EventArgs e)
        {
            if (SelectImageDialog.ShowDialog() == DialogResult.OK) {
                SetStatus("Loading Custom Sample Texture...");
                customSampler?.Dispose();
                customSampler = null;
                customSampler = (Bitmap)Image.FromFile(SelectImageDialog.FileName);
                SetCustomSamplerPathDisplay(SelectImageDialog.FileName);
                SetStatus();
            }
        }

        private void OnDoGreedyMeshing(object sender, EventArgs e)
        {
            if (origImage is null) {
                MessageBox.Show("Load an image first!");
                return;
            }

            SetStatus("Freeing Memory...");
            gfx?.Dispose();
            gfx = null;
            mesher?.Dispose();
            meshedImage?.Dispose();
            GC.Collect();

            SetStatus("Extracting Image Data...");
            mesher = new GreedyMesher(origImage, (int)MeshingTolerance.Value, UseOldColorTolerance.Checked);
            mesher.ExtractImagePixels();
            meshedImage = (Bitmap)origImage.Clone();
            GreedyMeshingDisplay.Image = meshedImage;
            SetStatus("Finding Meshes...");

            if (ProgressiveProcessing.Checked) {
                mesherEnumerator = mesher.GetNextMesh();
                IsMeshing = true;
                GreedyMeshUpdateTimer.Start();
            } else {
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();
                var meshes = mesher.DoGreedyMeshing();
                stopwatch.Stop();
                TimeSpan meshingTime = stopwatch.Elapsed;
                Debug.WriteLine($"Greedy Meshing Time: {stopwatch.Elapsed}");
                SetStatus("Rendering Meshes...");
                stopwatch.Restart();
                int doEventsCounter = 0;
                foreach (Rectangle mesh in meshes) {
                    DrawMesh(meshedImage, mesh);
                    ++doEventsCounter;
                    if (doEventsCounter > 1000000)
                        Application.DoEvents();
                }
                stopwatch.Stop();
                GreedyMeshingDisplay.Image = meshedImage;
                SetStatus();
                MessageBox.Show($"Greedy Meshing Completed!\nTotal meshes found: {mesher.MeshCount}\nFinding meshes time: {meshingTime}\nRendering time: {stopwatch.Elapsed}");
            }
            GC.Collect();
        }

        private void OnUpdateRefreshRate(object sender, EventArgs e)
        {
            GreedyMeshUpdateTimer.Interval = (int)MeshingUpdateRateSelector.Value;
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            if (IsMeshing) {
                bool finished = false;
                for (int i = 0; i < MeshesPerTick.Value; i++) {
                    if (!mesherEnumerator.MoveNext()) {
                        finished = true;
                        break;
                    }
                    Rectangle mesh = mesherEnumerator.Current;
                    if ((mesh.Width + 1) * (mesh.Height + 1) > (int)Math.Ceiling(MeshSizeThresholdInput.Value)) {
                        GreedyMeshingDisplay.Image = DrawMesh(meshedImage, mesh);
                    }
                }
                if (finished) {
                    IsMeshing = false;
                    GreedyMeshUpdateTimer.Stop();
                    meshedImage.Save(Environment.CurrentDirectory + "/meshedImage.png");
                    SetStatus();
                    MessageBox.Show($"Greedy Meshing Completed!\nTotal meshes found: {mesher.MeshCount}");
                }
            }
        }

        private Bitmap DrawMesh(Bitmap image, Rectangle mesh)
        {
            //Debug.WriteLine(mesh.ToString());
            if (gfx is null)
                gfx = Graphics.FromImage(image);
            if ((mesh.Width + 1) * (mesh.Height + 1) < (int)Math.Ceiling(MeshSizeThresholdInput.Value)) {
                return image;
            }
            mesh.Width += 1;
            mesh.Height += 1;
            if (UseRandomColor.Checked)
                gfx.FillRectangle(CreatePenWithRandomColor().Brush, mesh);
            else if (UseGeneratedColor.Checked)
                gfx.FillRectangle(CreatePenWithGeneratedColor().Brush, mesh);
            else if (UseCustomTexture.Checked) {
                if (customSampler is null) {
                    StopGreedyMeshing();
                    MessageBox.Show("No Custom Sample Texture Loaded!");
                    return image;
                }
                Point point = mesh.Location;
                PointF uv = new PointF(point.X / (float)meshedImage.Width, point.Y / (float)meshedImage.Height);
                Point samplePoint = new Point((int)(customSampler.Width * uv.X), (int)(customSampler.Height * uv.Y));
                gfx.FillRectangle(new Pen(customSampler.GetPixel(samplePoint.X, samplePoint.Y)).Brush, mesh);
            }
            else {
                Point point = mesh.Location;
                gfx.FillRectangle(new Pen(origImage.GetPixel(point.X, point.Y)).Brush, mesh);
            }
            gfx.Flush(System.Drawing.Drawing2D.FlushIntention.Sync);
            return image;
        }

        private Pen CreatePenWithGeneratedColor()
        {
            int r, g, b;
            b = color.G >= 240 ? (color.B + 10 < 256? color.B + 10 : 0) : color.B;
            g = color.R >= 240 ? (color.G + 10 < 256? color.G + 10 : 0)  : color.G;
            r = color.R < 250 ? color.R + 10 : 0;
            color = Color.FromArgb(255, r, g, b);
            return new Pen(color);
        }

        private Pen CreatePenWithRandomColor()
        {
            var color = Color.FromArgb(255, rng.Next(0, 256), rng.Next(0, 256), rng.Next(0, 256));
            return new Pen(color);
        }

        private void OnStopGreedyMeshing(object sender, EventArgs e)
        {
            StopGreedyMeshing();
        }

        private void OnSaveOutput(object sender, EventArgs e)
        {
            if (SaveOutputDialog.ShowDialog() == DialogResult.OK) {
                SetStatus("Saving Output...");
                meshedImage.Save(SaveOutputDialog.FileName, ImageFormat.Png);
                SetStatus();
            }
        }

        private void SetStatus(string status = null)
        {
            if (string.IsNullOrEmpty(status)) {
                Text = Title;
                StatusDisplay.Text = "Status: Idle";
            } else {
                Text = $"{Title} - {status}";
                StatusDisplay.Text = "Status: " + status;
            }
            Application.DoEvents();
        }

        private void SetCustomSamplerPathDisplay(string path = null)
        {
            if (string.IsNullOrEmpty(path))
                path = "No Custom Sampler Texture Loaded";
            CustomSamplerTexPathDisplay.Text = $"Custom Sampler Texture Path: {path}";
        }

        private void OnUnloadCustomSampler(object sender, EventArgs e)
        {
            customSampler?.Dispose();
            customSampler = null;
            SetCustomSamplerPathDisplay();
        }
    }
}
