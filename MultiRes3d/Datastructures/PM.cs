﻿using SlimDX;
using SlimDX.Direct3D11;
using SlimDX.DXGI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Buffer = SlimDX.Direct3D11.Buffer;
using MapFlags = SlimDX.Direct3D11.MapFlags;

namespace MultiRes3d {
	/// <summary>
	/// Repräsentiert ein renderbares Progressive Mesh Objekt.
	/// </summary>
	public class PM : Entity, IDisposable {
		bool disposed;
		Buffer vertexBuffer, indexBuffer;
		InputLayout inputLayout;
		VertexBufferBinding vertexBufferBinding;
		Viewport3d viewport3d;
		Mesh mesh;
		int numberOfSplits;

		public int NumberOfSplits {
			get {
				return numberOfSplits;
			}
		}

		public int CurrentSplit {
			get {
				return numberOfSplits - mesh.Splits.Count;
			}
		}

		public IList<Vertex> Vertices {
			get {
				return mesh.Vertices;
			}
		}

		public IList<Triangle> Faces {
			get {
				return mesh.Faces;
			}
		}

		/// <summary>
		/// Initialisiert eine neue Instanz der PM Klasse.
		/// </summary>
		/// <param name="viewport3d">
		/// Das D3D11 Viewport3d Control für welche die Instanz erzeugt wird.
		/// </param>
		/// <param name="m">
		/// Eine Mesh Instanz mit deren Daten die Progressiv Mesh initialisiert werden soll.
		/// </param>
		public PM(Viewport3d viewport3d, Mesh m) : base() {
			mesh = m;
			numberOfSplits = mesh.Splits.Count;
			this.viewport3d = viewport3d;
			// Platz im Vertexbuffer: Anzahl der Vertices der Grundmesh +
			//						  Anzahl der SplitRecords.
			vertexBuffer = CreateVertexBuffer(m.Vertices.Count + m.Splits.Count, m.Vertices);
			// Indexbuffer analog dazu.
			indexBuffer = CreateIndexBuffer((m.Splits.Count * 2 + m.Faces.Count) * 3, m.Faces);
			inputLayout = CreateInputLayout();
			vertexBufferBinding = new VertexBufferBinding(vertexBuffer, Vertex.Size, 0);

		}

		public void IncreaseDetail() {
			var ctx = viewport3d.Context;
			mesh.PerformVertexSplit();

			// Upload new data to vram.
			DataBox db = ctx.MapSubresource(vertexBuffer, MapMode.WriteDiscard, MapFlags.None);
			using (var s = db.Data) {
				s.WriteRange<Vertex>(mesh.Vertices.ToArray());
			}
			ctx.UnmapSubresource(vertexBuffer, 0);

			var indices = new uint[mesh.Faces.Count * 3];
			for (int i = 0; i < mesh.Faces.Count; i++) {
				for (int c = 0; c < 3; c++) {
					indices[i * 3 + c] = Convert.ToUInt32(mesh.Faces[i].Indices[c]);
				}
			}
			db = ctx.MapSubresource(indexBuffer, MapMode.WriteDiscard, MapFlags.None);
			using (var s = db.Data) {
				s.WriteRange<uint>(indices);
			}
			ctx.UnmapSubresource(indexBuffer, 0);


		}

		/// <summary>
		/// Rendert die Instanz.
		/// </summary>
		/// <param name="context">
		/// Der D3D11 Device Context.
		/// </param>
		/// <param name="effect">
		/// Die Effekt Instanz zum Setzen der benötigten Shader Variablen.
		/// </param>
		public override void Render(DeviceContext context, BasicEffect effect) {
			// D3D11 Input-Assembler konfigurieren.
			ApplyRenderState(context, effect);

			// Inhalt des Vertexbuffers rendern.
			var tech = effect.ColorLitTech;
			for (int i = 0; i < tech.Description.PassCount; i++) {
				tech.GetPassByIndex(i).Apply(context);

				context.DrawIndexed(mesh.Faces.Count * 3, 0, 0);
			}
		}

		/// <summary>
		/// Gibt die Resourcen der Instanz frei.
		/// </summary>
		public void Dispose() {
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		/// <summary> 
		/// Gibt die Resourcen der Instanz frei.
		/// </summary>
		/// <param name="disposing">
		/// true, um managed Resourcen freizugeben; andernfalls false.
		/// </param>
		protected virtual void Dispose(bool disposing) {
			if (!disposed) {
				// Merken, daß die Instanz freigegeben wurde.
				disposed = true;
				// Managed Resourcen freigeben.
				if (disposing) {
					if (vertexBuffer != null)
						vertexBuffer.Dispose();
					if (indexBuffer != null)
						indexBuffer.Dispose();
					if (inputLayout != null)
						inputLayout.Dispose();
				}
				// Hier Unmanaged Resourcen freigeben.
			}
		}

		/// <summary>
		/// Konfiguriert den InputAssembler des D3D11 Contexts, d.h. "verdrahtet"
		/// die Vertex- u. Indexbuffer der PM so daß sie als Eingabe für die
		/// Shader dienen.
		/// </summary>
		/// <param name="context">
		/// Der D3D11 Context.
		/// </param>
		/// <param name="effect">
		/// Die Effekt Instanz zum Setzen der benötigten Shader Variablen.
		/// </param>
		void ApplyRenderState(DeviceContext context, BasicEffect effect) {
			context.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
			context.InputAssembler.InputLayout = inputLayout;
			context.InputAssembler.SetVertexBuffers(0, vertexBufferBinding);
				context.InputAssembler.SetIndexBuffer(indexBuffer, Format.R32_UInt, 0);
			// Grundfarbe, die als Eingabewert bei der Lichtberechnung benutzt wird.
			effect.SetColor(Color.Gray);
		}


		#region D3D11 Initialisierungen
		/// <summary>
		/// Erstellt den Vertexbuffer für die Vertex-Daten der Mesh.
		/// </summary>
		/// <param name="maxVertices">
		/// Die max. Anzahl an Vertices, die im Buffer gespeichert können werden sollen.
		/// </param>
		/// <param name="vertices"></param>
		/// <returns>
		/// Eine initialisierte Instanz der Buffer-Klasse, die den VertexBuffer
		/// repräsentiert.
		/// </returns>
		Buffer CreateVertexBuffer(int maxVertices, IList<Vertex> vertices) {
			var desc = new BufferDescription(Vertex.Size * maxVertices,
				ResourceUsage.Dynamic, BindFlags.VertexBuffer, CpuAccessFlags.Write,
				ResourceOptionFlags.None, 0);
			var buf = new Buffer(viewport3d.Device, desc);
//				new DataStream(vertices.ToArray(), true, false), desc);
			var dbox = viewport3d.Context.MapSubresource(buf, MapMode.WriteDiscard, MapFlags.None);
			using (var s = dbox.Data) {
				s.WriteRange<Vertex>(vertices.ToArray());
			}
			viewport3d.Context.UnmapSubresource(buf, 0);

			return buf;
		}

		/// <summary>
		/// Erstellt den Indexbuffer für die Indices der Facetten der Mesh.
		/// </summary>
		/// <param name="maxIndices">
		/// Die max. Anzahl an Indices, die im Buffer gespeichert können werden sollen.
		/// </param>
		/// <param name="faces"></param>
		/// <returns>
		/// Eine initialisierte Instanz der Buffer-Klasse, die den IndexBuffer
		/// repräsentiert.
		/// </returns>
		Buffer CreateIndexBuffer(int maxIndices, IList<Triangle> faces) {
			var desc = new BufferDescription(sizeof(uint) * maxIndices,
				ResourceUsage.Dynamic, BindFlags.IndexBuffer, CpuAccessFlags.Write,
				ResourceOptionFlags.None, 0);
			var buf = new Buffer(viewport3d.Device, desc);
			var indices = new uint[faces.Count * 3];
			for(int i = 0; i < faces.Count; i++) {
				for(int c = 0; c < 3; c++) {
					indices[i * 3 + c] = Convert.ToUInt32(faces[i].Indices[c]);
				}
			}
			var dbox = viewport3d.Context.MapSubresource(buf, MapMode.WriteDiscard, MapFlags.None);
			using (var s = dbox.Data) {
				s.WriteRange<uint>(indices);
			}
			viewport3d.Context.UnmapSubresource(buf, 0);
			return buf;
		}

		/// <summary>
		/// Erstellt das Input-Layout für den Input-Assembler.
		/// </summary>
		/// <returns>
		/// Das Input-Layout.
		/// </returns>
		InputLayout CreateInputLayout() {
			// Pro-Vertex Daten im Vertexbuffer.
			var elements = new[] {
				new InputElement("Position", 0, Format.R32G32B32_Float, 0, 0, 
					InputClassification.PerVertexData, 0),
				new InputElement("Normal", 0, Format.R32G32B32_Float, 4 * 3, 0,
					InputClassification.PerVertexData, 0)
			};
			// Input-Layout wird gegen die Signatur der Shader-Technique geprüft, um
			// sicherzustellen, daß unsere Datenstruktur und die struct im Shader/Effect
			// übereinstimmen.
			var sig = viewport3d.Effect.ColorLitTech.GetPassByIndex(0).Description.Signature;

			return new InputLayout(viewport3d.Device, sig, elements);
		}
		#endregion
	}
}
