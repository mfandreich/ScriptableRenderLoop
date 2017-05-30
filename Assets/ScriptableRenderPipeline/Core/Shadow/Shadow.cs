using UnityEngine.Rendering;
using System;


namespace UnityEngine.Experimental.Rendering
{
    using ShadowRequestVector = VectorArray<ShadowmapBase.ShadowRequest>;
    using ShadowDataVector    = VectorArray<ShadowData>;
    using ShadowPayloadVector = VectorArray<ShadowPayload>;
    using ShadowIndicesVector = VectorArray<int>;
    using ShadowAlgoVector    = VectorArray<GPUShadowAlgorithm>;

    // Standard shadow map atlas implementation using one large shadow map
    public class ShadowAtlas : ShadowmapBase, IDisposable
    {
        public const uint k_MaxCascadesInShader = 4;
        protected readonly RenderTexture              m_Shadowmap;
        protected readonly RenderTargetIdentifier     m_ShadowmapId;
        protected readonly int                        m_TempDepthId;
        protected          VectorArray<CachedEntry>   m_EntryCache = new VectorArray<CachedEntry>( 0, true );
        protected          uint                       m_ActiveEntriesCount;
        protected          FrameId                    m_FrameId;
        protected          string                     m_ShaderKeyword;
        protected          int                        m_CascadeCount;
        protected          Vector3                    m_CascadeRatios;
        protected          uint                       m_TexSlot;
        protected          uint                       m_SampSlot;
        protected          uint[]                     m_TmpWidths  = new uint[ShadowmapBase.ShadowRequest.k_MaxFaceCount];
        protected          uint[]                     m_TmpHeights = new uint[ShadowmapBase.ShadowRequest.k_MaxFaceCount];
        protected          Vector4[]                  m_TmpSplits  = new Vector4[k_MaxCascadesInShader];
        protected          ShadowAlgoVector           m_SupportedAlgorithms = new ShadowAlgoVector( 0, false );

        protected struct Key
        {
            public int  id;
            public uint faceIdx;
            public int  visibleIdx;
            public uint shadowDataIdx;
        }

        protected struct Data
        {
            public FrameId              frameId;
            public int                  contentHash;
            public GPUShadowAlgorithm   shadowAlgo;
            public uint                 slice;
            public Rect                 viewport;
            public Matrix4x4            view;
            public Matrix4x4            proj;
            public Vector4              lightDir;
            public ShadowSplitData      splitData;

            public bool IsValid() { return viewport.width > 0 && viewport.height > 0; }
        }
        protected struct CachedEntry : IComparable<CachedEntry>
        {
            public Key  key;
            public Data current;
            public Data previous;

            public int CompareTo( CachedEntry other )
            {
                if (current.viewport.height != other.current.viewport.height)
                    return current.viewport.height > other.current.viewport.height ? -1 : 1;
                if (current.viewport.width != other.current.viewport.width)
                    return current.viewport.width > other.current.viewport.width ? -1 : 1;
                if( key.id != other.key.id )
                    return key.id < other.key.id ? -1 : 1;

                return key.faceIdx != other.key.faceIdx ? (key.faceIdx < other.key.faceIdx ? -1 : 1) : 0;
            }
        };

        public struct AtlasInit
        {
            public BaseInit baseInit;           // the base class's initializer
            public string   shaderKeyword;      // the global shader keyword to use when rendering the shadowmap
            public int      cascadeCount;       // the number of cascades to use (these are global in ShadowSettings for now for some reason)
            public Vector3  cascadeRatios;      // cascade split ratios
        }

        // UI stuff
        protected struct ValRange
        {
            GUIContent  Name;
            float       ValMin;
            float       ValDef;
            float       ValMax;
            float       ValScale;

            public ValRange( string name, float valMin, float valDef, float valMax, float valScale ) { Name = new GUIContent( name ); ValMin = valMin; ValDef = valDef; ValMax = valMax; ValScale = valScale; }
#if UNITY_EDITOR
            public void Slider( ref int currentVal ) { currentVal = ShadowUtils.Asint( ValScale * UnityEditor.EditorGUILayout.Slider( Name, ShadowUtils.Asfloat( currentVal ) / ValScale, ValMin, ValMax ) ); }
#else
            public void Slider( ref int currentVal ) {}
#endif
            public int Default() { return ShadowUtils.Asint( ValScale * ValDef ); }
        }
        readonly ValRange m_DefPCF_DepthBias = new ValRange( "Depth Bias", 0.0f, 0.05f, 1.0f, 000.1f );
        readonly ValRange m_DefPCF_FilterSize = new ValRange( "Filter Size", 1.0f, 1.0f, 10.0f, 1.0f );


        public ShadowAtlas( ref AtlasInit init ) : base( ref init.baseInit )
        {
            m_Shadowmap             = new RenderTexture( (int) m_Width, (int) m_Height, (int) m_ShadowmapBits, m_ShadowmapFormat, RenderTextureReadWrite.Linear );
            m_Shadowmap.hideFlags   = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            m_Shadowmap.dimension   = TextureDimension.Tex2DArray;
            m_Shadowmap.volumeDepth = (int) m_Slices;
            m_ShadowmapId           = new RenderTargetIdentifier( m_Shadowmap );

            if( !IsNativeDepth() )
            {
                m_TempDepthId = Shader.PropertyToID( "Temporary Shadowmap Depth" );
            }

            Initialize( init );
        }

        override protected void Register( GPUShadowType type, ShadowRegistry registry )
        {
            ShadowPrecision precision = m_ShadowmapBits == 32 ? ShadowPrecision.High : ShadowPrecision.Low;
            m_SupportedAlgorithms.Reserve( 5 );
            m_SupportedAlgorithms.AddUniqueUnchecked( ShadowUtils.Pack( ShadowAlgorithm.PCF, ShadowVariant.V0, precision ) );
            m_SupportedAlgorithms.AddUniqueUnchecked( ShadowUtils.Pack( ShadowAlgorithm.PCF, ShadowVariant.V1, precision ) );
            m_SupportedAlgorithms.AddUniqueUnchecked( ShadowUtils.Pack( ShadowAlgorithm.PCF, ShadowVariant.V2, precision ) );
            m_SupportedAlgorithms.AddUniqueUnchecked( ShadowUtils.Pack( ShadowAlgorithm.PCF, ShadowVariant.V3, precision ) );
            m_SupportedAlgorithms.AddUniqueUnchecked( ShadowUtils.Pack( ShadowAlgorithm.PCF, ShadowVariant.V4, precision ) );

            ShadowRegistry.VariantDelegate del = ( Light l, ShadowAlgorithm dataAlgorithm, ShadowVariant dataVariant, ShadowPrecision dataPrecision, ref int[] dataBlock ) =>
                {
                    CheckDataIntegrity( dataAlgorithm, dataVariant, dataPrecision, ref dataBlock );

                    m_DefPCF_DepthBias.Slider( ref dataBlock[0] );
                    if( dataVariant == ShadowVariant.V1 )
                        m_DefPCF_FilterSize.Slider( ref dataBlock[1] );
                };
            registry.Register( type, precision, ShadowAlgorithm.PCF, "Percentage Closer Filtering (PCF)",
                new ShadowVariant[]{ ShadowVariant.V0, ShadowVariant.V1, ShadowVariant.V2, ShadowVariant.V3, ShadowVariant.V4 },
                new string[]{"1 tap", "9 tap adaptive", "tent 3x3 (4 taps)", "tent 5x5 (9 taps)", "tent 7x7 (16 taps)" },
                new ShadowRegistry.VariantDelegate[] { del, del, del, del, del } );
        }
        // returns true if the original data passed integrity checks, false if the data had to be modified
        virtual protected bool CheckDataIntegrity( ShadowAlgorithm algorithm, ShadowVariant variant, ShadowPrecision precision, ref int[] dataBlock )
        {
            if( algorithm != ShadowAlgorithm.PCF ||
                (variant != ShadowVariant.V0 &&
                 variant != ShadowVariant.V1 &&
                 variant != ShadowVariant.V2 &&
                 variant != ShadowVariant.V3 &&
                 variant != ShadowVariant.V4))
                return true;

            const int k_BlockSize = 2;
            if( dataBlock == null || dataBlock.Length != k_BlockSize )
            {
                // set defaults
                dataBlock = new int[k_BlockSize];
                dataBlock[0] = m_DefPCF_DepthBias.Default();
                dataBlock[1] = m_DefPCF_FilterSize.Default();
                return false;
            }
            return true;
        }

        public void Initialize( AtlasInit init )
        {
            m_ShaderKeyword      = init.shaderKeyword;
            m_CascadeCount       = init.cascadeCount;
            m_CascadeRatios      = init.cascadeRatios;
        }

        public override void InitializeHack(int cascadeCound, Vector3 cascadeRatios)
        {
            m_CascadeCount = cascadeCound;
            m_CascadeRatios = cascadeRatios;
        }

        override public void ReserveSlots( ShadowContextStorage sc )
        {
            m_TexSlot = sc.RequestTex2DArraySlot();
            m_SampSlot = IsNativeDepth() ? sc.RequestSamplerSlot( m_CompSamplerState ) : sc.RequestSamplerSlot( m_SamplerState );
        }

        override public void Fill( ShadowContextStorage cs )
        {
            cs.SetTex2DArraySlot( m_TexSlot, m_ShadowmapId );
        }

        public void Dispose()
        {
            if( m_Shadowmap != null )
                m_Shadowmap.Release();
        }

        override public bool Reserve( FrameId frameId, ref ShadowData shadowData, ShadowRequest sr, uint width, uint height, ref VectorArray<ShadowData> entries, ref VectorArray<ShadowPayload> payload, VisibleLight[] lights )
        {
            for( uint i = 0, cnt = sr.facecount; i < cnt; ++i )
            {
                m_TmpWidths[i]  = width;
                m_TmpHeights[i] = height;
            }
            return Reserve( frameId, ref shadowData, sr, m_TmpWidths, m_TmpHeights, ref entries, ref payload, lights );
        }

        override public bool Reserve( FrameId frameId, ref ShadowData shadowData, ShadowRequest sr, uint[] widths, uint[] heights, ref VectorArray<ShadowData> entries, ref VectorArray<ShadowPayload> payload, VisibleLight[] lights )
        {
            if( m_FrameId.frameCount != frameId.frameCount )
                m_ActiveEntriesCount = 0;

            m_FrameId = frameId;

            uint algoIdx;
            GPUShadowAlgorithm shadowAlgo = sr.shadowAlgorithm;
            if( !m_SupportedAlgorithms.FindFirst( out algoIdx, ref shadowAlgo ) )
                return false;

            ShadowData  sd    = shadowData;
            ShadowData  dummy = new ShadowData();

            if( sr.shadowType != GPUShadowType.Point && sr.shadowType != GPUShadowType.Spot && sr.shadowType != GPUShadowType.Directional )
                return false;

            if( sr.shadowType == GPUShadowType.Directional )
            {
                for( uint i = 0; i < k_MaxCascadesInShader; ++i )
                    m_TmpSplits[i].Set( 0.0f, 0.0f, 0.0f, float.NegativeInfinity );
            }

            Key key;
            key.id            = sr.instanceId;
            key.faceIdx       = 0;
            key.visibleIdx    = (int)sr.index;
            key.shadowDataIdx = entries.Count();

            uint originalEntryCount    = entries.Count();
            uint originalPayloadCount  = payload.Count();
            uint originalActiveEntries = m_ActiveEntriesCount;

            uint facecnt  = sr.facecount;
            uint facemask = sr.facemask;
            uint bit      = 1;
            int  resIdx   = 0;
            bool multiFace= sr.shadowType != GPUShadowType.Spot;

            const uint k_MaxShadowDatasPerLight = 7; // 1 shared ShadowData and up to 6 faces for point lights
            entries.Reserve( k_MaxShadowDatasPerLight );

            float   nearPlaneOffset = QualitySettings.shadowNearPlaneOffset;

            GPUShadowAlgorithm sanitizedAlgo = ShadowUtils.ClearPrecision( sr.shadowAlgorithm );

            if( multiFace )
            {
                // For lights with multiple faces, the first shadow data contains
                // per light information, so not all fields contain valid data.
                // Shader code must make sure to read per face data from per face entries.
                sd.texelSizeRcp = new Vector2( m_WidthRcp, m_HeightRcp );
                sd.PackShadowType( sr.shadowType, sanitizedAlgo );
                sd.payloadOffset = payload.Count();
                entries.AddUnchecked( sd );
                key.shadowDataIdx++;
            }
            payload.Resize( payload.Count() + ReservePayload( sr ) );

            while ( facecnt > 0 )
            {
                if( (bit & facemask) != 0 )
                {
                    uint width  = widths[resIdx];
                    uint height = heights[resIdx];
                    uint ceIdx;
                    if( !Alloc( frameId, key, width, height, out ceIdx, payload ) )
                    {
                        entries.Purge( entries.Count() - originalEntryCount );
                        payload.Purge( payload.Count() - originalPayloadCount );
                        uint added = m_ActiveEntriesCount - originalActiveEntries;
                        for( uint i = originalActiveEntries; i < m_ActiveEntriesCount; ++i )
                            m_EntryCache.Swap( i, m_EntryCache.Count()-i-1 );
                        m_EntryCache.Purge( added, Free );
                        m_ActiveEntriesCount = originalActiveEntries;
                        return false;
                    }

                    // read
                    CachedEntry ce = m_EntryCache[ceIdx];
                    // modify
                    Matrix4x4 vp;
                    if( sr.shadowType == GPUShadowType.Point )
                        vp = ShadowUtils.ExtractPointLightMatrix( lights[sr.index], key.faceIdx, 2.0f, out ce.current.view, out ce.current.proj, out ce.current.lightDir, out ce.current.splitData );
                    else if( sr.shadowType == GPUShadowType.Spot )
                        vp = ShadowUtils.ExtractSpotLightMatrix( lights[sr.index], out ce.current.view, out ce.current.proj, out ce.current.lightDir, out ce.current.splitData );
                    else if( sr.shadowType == GPUShadowType.Directional )
                    {
                        vp = ShadowUtils.ExtractDirectionalLightMatrix( lights[sr.index], key.faceIdx, m_CascadeCount, m_CascadeRatios, nearPlaneOffset, width, height, out ce.current.view, out ce.current.proj, out ce.current.lightDir, out ce.current.splitData, m_CullResults, (int) sr.index );
                        m_TmpSplits[key.faceIdx]    = ce.current.splitData.cullingSphere;
                        if( ce.current.splitData.cullingSphere.w != float.NegativeInfinity )
                            m_TmpSplits[key.faceIdx].w *= ce.current.splitData.cullingSphere.w;
                    }
                    else
                        vp = Matrix4x4.identity; // should never happen, though
                    // write :(
                    ce.current.shadowAlgo = shadowAlgo;
                    m_EntryCache[ceIdx] = ce;

                    sd.worldToShadow = vp.transpose; // apparently we need to transpose matrices that are sent to HLSL
                    sd.scaleOffset   = new Vector4( ce.current.viewport.width * m_WidthRcp, ce.current.viewport.height * m_HeightRcp, ce.current.viewport.x, ce.current.viewport.y );
                    sd.texelSizeRcp  = new Vector4( m_WidthRcp, m_HeightRcp, 1.0f / ce.current.viewport.width, 1.0f / ce.current.viewport.height );
                    sd.PackShadowmapId( m_TexSlot, m_SampSlot, ce.current.slice );
                    sd.PackShadowType( sr.shadowType, sanitizedAlgo );
                    sd.payloadOffset = originalPayloadCount;
                    entries.AddUnchecked( sd );

                    resIdx++;
                    facecnt--;
                    key.shadowDataIdx++;
                }
                else
                {
                    // we push a dummy face in, otherwise we'd need a mapping from face index to shadowData in the shader as well
                    entries.AddUnchecked( dummy );
                }
                key.faceIdx++;
                bit <<= 1;
            }

            WritePerLightPayload( ref lights[sr.index], sr, ref sd, ref payload, ref originalPayloadCount );

            return true;
        }

        // Returns how many entries will be written into the payload buffer per light.
        virtual protected uint ReservePayload( ShadowRequest sr )
        {
            uint payloadSize  = sr.shadowType == GPUShadowType.Directional ? k_MaxCascadesInShader : 0;
                 payloadSize += ShadowUtils.ExtractAlgorithm( sr.shadowAlgorithm ) == ShadowAlgorithm.PCF ? 1u : 0;
            return payloadSize;
        }

        // Writes additional per light data into the payload vector. Make sure to call base.WritePerLightPayload first.
        virtual protected void WritePerLightPayload( ref VisibleLight light, ShadowRequest sr, ref ShadowData sd, ref ShadowPayloadVector payload, ref uint payloadOffset )
        {
            ShadowPayload sp = new ShadowPayload();
            if( sr.shadowType == GPUShadowType.Directional )
            {
                for( uint i = 0; i < k_MaxCascadesInShader; i++, payloadOffset++ )
                {
                    sp.Set( m_TmpSplits[i] );
                    payload[payloadOffset] = sp;
                }
            }
            ShadowAlgorithm algo; ShadowVariant vari; ShadowPrecision prec;
            ShadowUtils.Unpack( sr.shadowAlgorithm, out algo, out vari, out prec );
            if( algo == ShadowAlgorithm.PCF )
            {
                AdditionalLightData ald = light.light.GetComponent<AdditionalLightData>();
                if( !ald )
                    return;

                int shadowDataFormat;
                int[] shadowData = ald.GetShadowData( out shadowDataFormat );
                if( !CheckDataIntegrity( algo, vari, prec, ref shadowData ) )
                {
                    ald.SetShadowAlgorithm( (int)algo, (int)vari, (int) prec, shadowDataFormat, shadowData );
                    Debug.Log( "Fixed up shadow data for algorithm " + algo + ", variant " + vari );
                }

                switch( vari )
                {
                case ShadowVariant.V0:
                case ShadowVariant.V1:
                case ShadowVariant.V2:
                case ShadowVariant.V3:
                case ShadowVariant.V4:
                    {
                        sp.Set( shadowData[0] | (SystemInfo.usesReversedZBuffer ? 1 : 0), shadowData[1], 0, 0 );
                        payload[payloadOffset] = sp;
                        payloadOffset++;
                    }
                break;
                }
            }
        }

        override public bool ReserveFinalize( FrameId frameId, ref VectorArray<ShadowData> entries, ref VectorArray<ShadowPayload> payload )
        {
            if( Layout() )
            {
                // patch up the shadow data contents with the result of the layouting step
                for( uint i = 0; i < m_ActiveEntriesCount; ++i )
                {
                    CachedEntry ce = m_EntryCache[i];

                    ShadowData sd = entries[ce.key.shadowDataIdx];
                    // update the shadow data with the actual result of the layouting step
                    sd.scaleOffset   = new Vector4( ce.current.viewport.width * m_WidthRcp, ce.current.viewport.height * m_HeightRcp, ce.current.viewport.x * m_WidthRcp, ce.current.viewport.y * m_HeightRcp );
                    sd.PackShadowmapId( m_TexSlot, m_SampSlot, ce.current.slice );
                    // write back the correct results
                    entries[ce.key.shadowDataIdx] = sd;
                }
                m_EntryCache.Purge(m_EntryCache.Count() - m_ActiveEntriesCount, (CachedEntry entry) => { Free(entry); });
                return true;
            }
            m_ActiveEntriesCount  = 0;
            m_EntryCache.Reset( (CachedEntry entry) => { Free(entry); });
            return false;
        }

        virtual protected void PreUpdate( FrameId frameId, CommandBuffer cb, uint rendertargetSlice )
        {
            cb.SetRenderTarget( m_ShadowmapId, 0, (CubemapFace) 0, (int) rendertargetSlice );
            if( !IsNativeDepth() )
            {
                cb.GetTemporaryRT(m_TempDepthId, (int)m_Width, (int)m_Height, (int)m_ShadowmapBits, FilterMode.Bilinear, RenderTextureFormat.Shadowmap, RenderTextureReadWrite.Default);
                cb.SetRenderTarget( new RenderTargetIdentifier( m_TempDepthId ) );
            }
            cb.ClearRenderTarget( true, !IsNativeDepth(), m_ClearColor );
        }

        override public void Update( FrameId frameId, ScriptableRenderContext renderContext, CullResults cullResults, VisibleLight[] lights )
        {
            var profilingSample = new HDPipeline.Utilities.ProfilingSample("Shadowmap" + m_TexSlot, renderContext);

            if (!string.IsNullOrEmpty( m_ShaderKeyword ) )
            {
                var cb = new CommandBuffer();
                cb.name = "Shadowmap.EnableShadowKeyword";
                cb.EnableShaderKeyword(m_ShaderKeyword);
                renderContext.ExecuteCommandBuffer( cb );
                cb.Dispose();
            }

            // loop for generating each individual shadowmap
            uint curSlice = uint.MaxValue;
            Bounds bounds;
            DrawShadowsSettings dss = new DrawShadowsSettings( cullResults, 0 );
            for( uint i = 0; i < m_ActiveEntriesCount; ++i )
            {
                if( !cullResults.GetShadowCasterBounds( m_EntryCache[i].key.visibleIdx, out bounds ) )
                    continue;

                var cb = new CommandBuffer();
                uint entrySlice = m_EntryCache[i].current.slice;
                if( entrySlice != curSlice )
                {
                    Debug.Assert( curSlice == uint.MaxValue || entrySlice >= curSlice, "Entries in the entry cache are not ordered in slice order." );
                    cb.name = "Shadowmap.Update.Slice" + entrySlice;

                    if( curSlice != uint.MaxValue )
                    {
                        PostUpdate( frameId, cb, curSlice, lights );
                    }
                    curSlice = entrySlice;
                    PreUpdate( frameId, cb, curSlice );
                }

                cb.name = "Shadowmap.Update - slice: " + curSlice + ", vp.x: " + m_EntryCache[i].current.viewport.x + ", vp.y: " + m_EntryCache[i].current.viewport.y + ", vp.w: " + m_EntryCache[i].current.viewport.width + ", vp.h: " + m_EntryCache[i].current.viewport.height;
                cb.SetViewport( m_EntryCache[i].current.viewport );
                cb.SetViewProjectionMatrices( m_EntryCache[i].current.view, m_EntryCache[i].current.proj );
                cb.SetGlobalVector( "g_vLightDirWs", m_EntryCache[i].current.lightDir );
                renderContext.ExecuteCommandBuffer( cb );
                cb.Dispose();

                dss.lightIndex = m_EntryCache[i].key.visibleIdx;
                dss.splitData = m_EntryCache[i].current.splitData;
                renderContext.DrawShadows( ref dss ); // <- if this was a call on the commandbuffer we would get away with using just once commandbuffer for the entire shadowmap, instead of one per face
            }

            // post update
            var cblast = new CommandBuffer();
            PostUpdate( frameId, cblast, curSlice, lights );
            if( !string.IsNullOrEmpty( m_ShaderKeyword ) )
            {
                cblast.name = "Shadowmap.DisableShaderKeyword";
                cblast.DisableShaderKeyword( m_ShaderKeyword );
            }
            renderContext.ExecuteCommandBuffer( cblast );
            cblast.Dispose();

            m_ActiveEntriesCount = 0;

            profilingSample.Dispose();
        }

        virtual protected void PostUpdate( FrameId frameId, CommandBuffer cb, uint rendertargetSlice, VisibleLight[] lights )
        {
            if( !IsNativeDepth() )
                cb.ReleaseTemporaryRT( m_TempDepthId );
        }

        protected bool Alloc( FrameId frameId, Key key, uint width, uint height, out uint cachedEntryIdx, VectorArray<ShadowPayload> payload )
        {
            CachedEntry ce = new CachedEntry();
            ce.key                 = key;
            ce.current.frameId     = frameId;
            ce.current.contentHash = -1;
            ce.current.slice       = 0;
            ce.current.viewport    = new Rect( 0, 0, width, height );

            uint idx;
            if ( m_EntryCache.FindFirst( out idx, ref key, (ref Key k, ref CachedEntry entry) => { return k.id == entry.key.id && k.faceIdx == entry.key.faceIdx; } ) )
            {
                if( m_EntryCache[idx].current.viewport.width == width && m_EntryCache[idx].current.viewport.height == height )
                {
                    ce.previous = m_EntryCache[idx].current;
                    m_EntryCache[idx] = ce;
                    cachedEntryIdx = m_ActiveEntriesCount;
                    m_EntryCache.SwapUnchecked( m_ActiveEntriesCount++, idx );
                    return true;
                }
                else
                {
                    m_EntryCache.SwapUnchecked( idx, m_EntryCache.Count()-1 );
                    m_EntryCache.Purge( 1, Free );
                }
            }

            idx = m_EntryCache.Count();
            m_EntryCache.Add( ce );
            cachedEntryIdx = m_ActiveEntriesCount;
            m_EntryCache.SwapUnchecked( m_ActiveEntriesCount++, idx );
            return true;
        }

        protected bool Layout()
        {
            VectorArray<CachedEntry> tmp = m_EntryCache.Subrange( 0, m_ActiveEntriesCount );
            tmp.Sort();

            float curx = 0, cury = 0, curh = 0, xmax = m_Width, ymax = m_Height;
            uint curslice = 0;

            for (uint i = 0; i < m_ActiveEntriesCount; ++i)
            {
                // shadow atlas layouting
                CachedEntry ce = m_EntryCache[i];
                Rect vp = ce.current.viewport;

                if( curx + vp.width > xmax )
                {
                    curx = 0;
                    cury += curh;
                }
                if( curx + vp.width > xmax || cury + curh > ymax )
                {
                    curslice++;
                    curx = 0;
                    cury = 0;
                }
                if( curx + vp.width > xmax || cury + curh > ymax || curslice == m_Slices )
                {
                    Debug.LogError( "ERROR! Shadow atlasing failed." );
                    return false;
                }
                vp.x = curx;
                vp.y = cury;
                ce.current.viewport = vp;
                ce.current.slice    = curslice;
                m_EntryCache[i]     = ce;
                curx += vp.width;
                curh = curh >= vp.height ? curh : vp.height;
            }
            return true;
        }

        protected void Free( CachedEntry ce )
        {
            // Nothing to do for this implementation here, as the atlas is reconstructed each frame, instead of keeping state across frames
        }
    }

// -------------------------------------------------------------------------------------------------------------------------------------------------
//
//                                                      ShadowVariance
//
// -------------------------------------------------------------------------------------------------------------------------------------------------

    // Shadowmap supporting various flavors of variance shadow maps.
    public class ShadowVariance : ShadowAtlas
    {
        protected const int     k_MomentBlurThreadsPerWorkgroup = 16;
        protected const int     k_BlurKernelMinSize             = 3;
        protected const int     k_BlurKernelDefSize             = (7 - k_BlurKernelMinSize) / 2;
        protected const int     k_BlurKernelMaxSize             = 17;
        protected const int     k_BlurKernelCount               = k_BlurKernelMaxSize / 2;
        protected ComputeShader m_MomentBlurCS;
        protected int[]         m_KernelVSM    = new int[k_BlurKernelCount];
        protected int[]         m_KernelEVSM_2 = new int[k_BlurKernelCount];
        protected int[]         m_KernelEVSM_4 = new int[k_BlurKernelCount];
        protected int[]         m_KernelMSM    = new int[k_BlurKernelCount];
        protected float[][]     m_BlurWeights  = new float[k_BlurKernelCount][];
        protected int           m_SampleCount;

        [Flags]
        protected enum Flags
        {
            bpp_16        = 1 << 0,
            channels_2    = 1 << 1,
        }
        protected readonly Flags m_Flags;

        // default values
        readonly ValRange m_DefVSM_LightLeakBias    = new ValRange( "Light leak bias"   , 0.0f, 0.5f    ,  0.99f , 1.0f   );
        readonly ValRange m_DefVSM_VarianceBias     = new ValRange( "Variance bias"     , 0.0f, 0.1f    ,  1.0f  , 0.01f  );
        readonly ValRange m_DefEVSM_LightLeakBias   = new ValRange( "Light leak bias"   , 0.0f, 0.0f    ,  0.99f , 1.0f   );
        readonly ValRange m_DefEVSM_VarianceBias    = new ValRange( "Variance bias"     , 0.0f, 0.1f    ,  1.0f  , 0.01f  );
        readonly ValRange m_DefEVSM_PosExponent_32  = new ValRange( "Positive Exponent" , 1.0f, 1.0f    , 42.0f  , 1.0f   );
        readonly ValRange m_DefEVSM_NegExponent_32  = new ValRange( "Negative Exponent" , 1.0f, 1.0f    , 42.0f  , 1.0f   );
        readonly ValRange m_DefEVSM_PosExponent_16  = new ValRange( "Positive Exponent" , 1.0f, 1.0f    ,  5.54f , 1.0f   );
        readonly ValRange m_DefEVSM_NegExponent_16  = new ValRange( "Negative Exponent" , 1.0f, 1.0f    ,  5.54f , 1.0f   );
        readonly ValRange m_DefMSM_LightLeakBias    = new ValRange( "Light leak bias"   , 0.0f, 0.5f    ,  0.99f , 1.0f   );
        readonly ValRange m_DefMSM_MomentBias       = new ValRange( "Moment Bias"       , 0.0f, 0.3f    ,  1.0f  , 0.0001f);
        readonly ValRange m_DefMSM_DepthBias        = new ValRange( "Depth Bias"        , 0.0f, 0.1f    ,  1.0f  , 0.1f   );

        public static RenderTextureFormat GetFormat( bool use_16_BitsPerChannel, bool use_2_Channels, bool use_MSM )
        {
            Debug.Assert( !use_2_Channels || !use_MSM ); // MSM always requires 4 channels
            if( use_16_BitsPerChannel )
            {
                return use_2_Channels ? RenderTextureFormat.RGHalf : (use_MSM ? RenderTextureFormat.ARGB64 : RenderTextureFormat.ARGBHalf);
            }
            else
            {
                return use_2_Channels ? RenderTextureFormat.RGFloat : RenderTextureFormat.ARGBFloat;
            }
        }
        public ShadowVariance( ref AtlasInit init ) : base( ref init )
        {
            m_Flags |= (base.m_ShadowmapFormat == RenderTextureFormat.ARGBHalf || base.m_ShadowmapFormat == RenderTextureFormat.RGHalf || base.m_ShadowmapFormat == RenderTextureFormat.ARGB64) ? Flags.bpp_16 : 0;
            m_Flags |= (base.m_ShadowmapFormat == RenderTextureFormat.RGFloat  || base.m_ShadowmapFormat == RenderTextureFormat.RGHalf) ? Flags.channels_2 : 0;

            m_Shadowmap.enableRandomWrite = true;
            m_SampleCount  = 1; // TODO: Unity can't bind msaa rts as textures, yet, so this has to remain 1 for now
            m_MomentBlurCS = Resources.Load<ComputeShader>( "ShadowBlurMoments" );

            if( m_MomentBlurCS )
            {
                for( int i = 0, blurSize = 3; i < k_BlurKernelCount; ++i, blurSize += 2 )
                {
                    m_KernelVSM[i]    = m_MomentBlurCS.FindKernel( "main_VSM_"      + blurSize );
                    m_KernelEVSM_2[i] = m_MomentBlurCS.FindKernel( "main_EVSM_2_"   + blurSize );
                    m_KernelEVSM_4[i] = m_MomentBlurCS.FindKernel( "main_EVSM_4_"   + blurSize );
                    m_KernelMSM[i]    = m_MomentBlurCS.FindKernel( "main_MSM_"      + blurSize );

                    m_BlurWeights[i] = new float[2+i];
                    FillBlurWeights( i );
                }
            }

            // normalize blur weights
            for( int i = 0; i < k_BlurKernelCount; ++i )
            {
                float weightSum = 0.0f;
                for( int j = 0; j < m_BlurWeights[i].Length; ++j )
                    weightSum += m_BlurWeights[i][j];
                weightSum = 1.0f / (2.0f * weightSum - m_BlurWeights[i][0]);
                for( int j = 0; j < m_BlurWeights[i].Length; ++j )
                    m_BlurWeights[i][j] *= weightSum;
            }
        }

        private void FillBlurWeights( int idx )
        {
            if( idx == 0 )
            {
                m_BlurWeights[0][0] = 2.0f;
                m_BlurWeights[0][1] = 1.0f;
                return;
            }

            float[] prev = m_BlurWeights[idx-1];
            float[] cur  = m_BlurWeights[idx];
            int prevSize = prev.Length;
            for( int i = 0; i < prevSize; ++i )
            {
                cur[i] = prev[Math.Abs( i-1 )] + prev[i] * 2.0f + (i == (prevSize-1) ? 0.0f : prev[i+1]);
            }
            cur[cur.Length-1] = 1.0f;
        }

        private void BlurSlider( ref int currentVal )
        {
#if UNITY_EDITOR
            currentVal = k_BlurKernelMinSize + currentVal * 2;
            currentVal = (int) Math.Round( UnityEditor.EditorGUILayout.Slider( "Blur Size", currentVal, k_BlurKernelMinSize, k_BlurKernelMaxSize ) );
            currentVal = (currentVal - k_BlurKernelMinSize) / 2;
#endif
        }

        override protected void Register( GPUShadowType type, ShadowRegistry registry )
        {
            bool supports_VSM    = m_ShadowmapFormat != RenderTextureFormat.ARGB64;
            bool supports_EVSM_2 = m_ShadowmapFormat != RenderTextureFormat.ARGB64;
            bool supports_EVSM_4 = m_ShadowmapFormat != RenderTextureFormat.ARGB64 && (m_Flags & Flags.channels_2) == 0;
            bool supports_MSM    = (m_Flags & Flags.channels_2) == 0 && ((m_Flags & Flags.bpp_16) == 0 || m_ShadowmapFormat == RenderTextureFormat.ARGB64);

            ShadowRegistry.VariantDelegate vsmDel = ( Light l, ShadowAlgorithm dataAlgorithm, ShadowVariant dataVariant, ShadowPrecision dataPrecision, ref int[] dataBlock ) =>
                {
                    CheckDataIntegrity( dataAlgorithm, dataVariant, dataPrecision, ref dataBlock );

                    m_DefVSM_LightLeakBias.Slider( ref dataBlock[0] );
                    m_DefVSM_VarianceBias.Slider( ref dataBlock[1] );
                    BlurSlider( ref dataBlock[2] );
                };

            ShadowRegistry.VariantDelegate evsmDel = ( Light l, ShadowAlgorithm dataAlgorithm, ShadowVariant dataVariant, ShadowPrecision dataPrecision, ref int[] dataBlock ) =>
                {
                    CheckDataIntegrity( dataAlgorithm, dataVariant, dataPrecision, ref dataBlock );

                    m_DefEVSM_LightLeakBias.Slider( ref dataBlock[0] );
                    m_DefEVSM_VarianceBias.Slider( ref dataBlock[1] );
                    if( (m_Flags & Flags.bpp_16) != 0 )
                        m_DefEVSM_PosExponent_16.Slider( ref dataBlock[2] );
                    else
                        m_DefEVSM_PosExponent_32.Slider( ref dataBlock[2] );
                    if( dataVariant == ShadowVariant.V1 )
                    {
                        if( (m_Flags & Flags.bpp_16) != 0 )
                            m_DefEVSM_NegExponent_16.Slider( ref dataBlock[3] );
                        else
                            m_DefEVSM_NegExponent_32.Slider( ref dataBlock[3] );
                    }
                    BlurSlider( ref dataBlock[4] );
                };

            ShadowRegistry.VariantDelegate msmDel = ( Light l, ShadowAlgorithm dataAlgorithm, ShadowVariant dataVariant, ShadowPrecision dataPrecision, ref int[] dataBlock ) =>
                {
                    CheckDataIntegrity( dataAlgorithm, dataVariant, dataPrecision, ref dataBlock );

                    m_DefMSM_LightLeakBias.Slider( ref dataBlock[0] );
                    m_DefMSM_MomentBias.Slider( ref dataBlock[1] );
                    m_DefMSM_DepthBias.Slider( ref dataBlock[2] );
                    BlurSlider( ref dataBlock[3] );
                };

            ShadowPrecision precision = (m_Flags & Flags.bpp_16) == 0 ? ShadowPrecision.High : ShadowPrecision.Low;
            m_SupportedAlgorithms.Reserve( 5 );
            if( supports_VSM )
            {
                m_SupportedAlgorithms.AddUniqueUnchecked( ShadowUtils.Pack( ShadowAlgorithm.VSM, ShadowVariant.V0, precision ) );
                registry.Register( type, precision, ShadowAlgorithm.VSM, "Variance shadow map (VSM)",
                    new ShadowVariant[] { ShadowVariant.V0 }, new string[] { "2 moments" }, new ShadowRegistry.VariantDelegate[] { vsmDel } );
            }
            if( supports_EVSM_2 && !supports_EVSM_4 )
            {
                m_SupportedAlgorithms.AddUniqueUnchecked( ShadowUtils.Pack( ShadowAlgorithm.EVSM, ShadowVariant.V0, precision ) );
                registry.Register( type, precision, ShadowAlgorithm.EVSM, "Exponential variance shadow map (EVSM)",
                    new ShadowVariant[] { ShadowVariant.V0 }, new string[] { "2 moments" }, new ShadowRegistry.VariantDelegate[] { evsmDel } );
            }
            if( supports_EVSM_4 )
            {
                m_SupportedAlgorithms.AddUniqueUnchecked( ShadowUtils.Pack( ShadowAlgorithm.EVSM, ShadowVariant.V0, precision ) );
                m_SupportedAlgorithms.AddUniqueUnchecked( ShadowUtils.Pack( ShadowAlgorithm.EVSM, ShadowVariant.V1, precision ) );
                registry.Register( type, precision, ShadowAlgorithm.EVSM, "Exponential variance shadow map (EVSM)",
                    new ShadowVariant[] { ShadowVariant.V0, ShadowVariant.V1 }, new string[] { "2 moments", "4 moments" }, new ShadowRegistry.VariantDelegate[] { evsmDel, evsmDel } );
            }
            if( supports_MSM )
            {
                m_SupportedAlgorithms.AddUniqueUnchecked( ShadowUtils.Pack( ShadowAlgorithm.MSM, ShadowVariant.V0, precision ) );
                m_SupportedAlgorithms.AddUniqueUnchecked( ShadowUtils.Pack( ShadowAlgorithm.MSM, ShadowVariant.V1, precision ) );
                registry.Register( type, precision, ShadowAlgorithm.MSM, "Moment shadow map (MSM)",
                    new ShadowVariant[] { ShadowVariant.V0, ShadowVariant.V1 }, new string[] { "Hamburg", "Hausdorff" }, new ShadowRegistry.VariantDelegate[] { msmDel, msmDel } );
            }
        }

        override protected bool CheckDataIntegrity( ShadowAlgorithm algorithm, ShadowVariant variant, ShadowPrecision precision, ref int[] dataBlock )
        {
            switch( algorithm )
            {
            case ShadowAlgorithm.VSM:
                {
                    const int k_BlockSize = 3;
                    if( dataBlock == null || dataBlock.Length != k_BlockSize )
                    {
                        // set defaults
                        dataBlock = new int[k_BlockSize];
                        dataBlock[0] = m_DefVSM_LightLeakBias.Default();
                        dataBlock[1] = m_DefVSM_VarianceBias.Default();
                        dataBlock[2] = k_BlurKernelDefSize;
                        return false;
                    }
                    return true;
                }
            case ShadowAlgorithm.EVSM:
                {
                    const int k_BlockSize = 5;
                    if( dataBlock == null || dataBlock.Length != k_BlockSize )
                    {
                        // set defaults
                        dataBlock = new int[k_BlockSize];
                        dataBlock[0] = m_DefEVSM_LightLeakBias.Default();
                        dataBlock[1] = m_DefEVSM_VarianceBias.Default();
                        dataBlock[2] = (m_Flags & Flags.bpp_16) != 0 ? m_DefEVSM_PosExponent_16.Default() : m_DefEVSM_PosExponent_32.Default();
                        dataBlock[3] = (m_Flags & Flags.bpp_16) != 0 ? m_DefEVSM_NegExponent_16.Default() : m_DefEVSM_NegExponent_32.Default();
                        dataBlock[4] = k_BlurKernelDefSize;
                        return false;
                    }
                    return true;
                }
            case ShadowAlgorithm.MSM:
                {
                    const int k_BlockSize = 4;
                    if( dataBlock == null || dataBlock.Length != k_BlockSize )
                    {
                        // set defaults
                        dataBlock = new int[k_BlockSize];
                        dataBlock[0] = m_DefMSM_LightLeakBias.Default();
                        dataBlock[1] = m_DefMSM_MomentBias.Default();
                        dataBlock[2] = m_DefMSM_DepthBias.Default();
                        dataBlock[3] = k_BlurKernelDefSize;
                        return false;
                    }
                    return true;
                }
            default: return base.CheckDataIntegrity( algorithm, variant, precision, ref dataBlock );
            }
        }


        override protected uint ReservePayload( ShadowRequest sr )
        {
            uint cnt = base.ReservePayload( sr );
            switch( ShadowUtils.ExtractAlgorithm( sr.shadowAlgorithm ) )
            {
            case ShadowAlgorithm.VSM : return cnt + 1;
            case ShadowAlgorithm.EVSM: return cnt + 1;
            case ShadowAlgorithm.MSM : return cnt + 1;
            default: return cnt;
            }
        }

        // Writes additional per light data into the payload vector. Make sure to call base.WritePerLightPayload first.
        override protected void WritePerLightPayload( ref VisibleLight light, ShadowRequest sr, ref ShadowData sd, ref ShadowPayloadVector payload, ref uint payloadOffset )
        {
            base.WritePerLightPayload( ref light, sr, ref sd, ref payload, ref payloadOffset );

            AdditionalLightData ald = light.light.GetComponent<AdditionalLightData>();
            if( !ald )
                return;

            ShadowPayload sp = new ShadowPayload();
            int shadowDataFormat;
            int[] shadowData = ald.GetShadowData( out shadowDataFormat );
            if( shadowData == null )
                return;

            ShadowAlgorithm algo;
            ShadowVariant vari;
            ShadowPrecision prec;
            ShadowUtils.Unpack( sr.shadowAlgorithm, out algo, out vari, out prec );
            CheckDataIntegrity( algo, vari, prec, ref shadowData );

            switch( algo )
            {
            case ShadowAlgorithm.VSM:
                {
                    sp.p0 = shadowData[0];
                    sp.p1 = shadowData[1];
                    payload[payloadOffset] = sp;
                    payloadOffset++;
                }
                break;
            case ShadowAlgorithm.EVSM:
                {
                    sp.p0 = shadowData[0];
                    sp.p1 = shadowData[1];
                    sp.p2 = shadowData[2];
                    sp.p3 = shadowData[3];

                    payload[payloadOffset] = sp;
                    payloadOffset++;
                }
                break;
            case ShadowAlgorithm.MSM:
                {
                    sp.Set( shadowData[0], shadowData[1], shadowData[2] | (SystemInfo.usesReversedZBuffer ? 1 : 0), (m_Flags & Flags.bpp_16) != 0 ? 0x3f800000 : 0 );
                    payload[payloadOffset] = sp;
                    payloadOffset++;
                }
                break;
            }
        }


        override protected void PreUpdate( FrameId frameId, CommandBuffer cb, uint rendertargetSlice )
        {
            cb.SetRenderTarget( m_ShadowmapId, 0, (CubemapFace) 0, (int) rendertargetSlice );
            cb.GetTemporaryRT( m_TempDepthId, (int) m_Width, (int) m_Height, (int) m_ShadowmapBits, FilterMode.Bilinear, RenderTextureFormat.Shadowmap, RenderTextureReadWrite.Default, m_SampleCount );
            cb.SetRenderTarget( new RenderTargetIdentifier( m_TempDepthId ) );
            cb.ClearRenderTarget( true, true, m_ClearColor );
        }

        protected override void PostUpdate( FrameId frameId, CommandBuffer cb, uint rendertargetSlice, VisibleLight[] lights )
        {
            cb.name = "VSM conversion";
            if ( rendertargetSlice == uint.MaxValue )
            {
                base.PostUpdate( frameId, cb, rendertargetSlice, lights );
                return;
            }

            uint cnt = m_EntryCache.Count();
            uint i = 0;
            while( i < cnt && m_EntryCache[i].current.slice < rendertargetSlice )
            {
                i++;
            }
            if( i >= cnt || m_EntryCache[i].current.slice > rendertargetSlice )
                return;


            int kernelIdx = 2;
            int currentKernel = 0;

            for( int j = 0; j < k_BlurKernelCount; ++j )
            {
                cb.SetComputeTextureParam( m_MomentBlurCS, m_KernelVSM[j]   , "depthTex" , new RenderTargetIdentifier( m_TempDepthId ) );
                cb.SetComputeTextureParam( m_MomentBlurCS, m_KernelVSM[j]   , "outputTex", m_ShadowmapId );
                cb.SetComputeTextureParam( m_MomentBlurCS, m_KernelEVSM_2[j], "depthTex" , new RenderTargetIdentifier( m_TempDepthId ) );
                cb.SetComputeTextureParam( m_MomentBlurCS, m_KernelEVSM_2[j], "outputTex", m_ShadowmapId );
                cb.SetComputeTextureParam( m_MomentBlurCS, m_KernelEVSM_4[j], "depthTex" , new RenderTargetIdentifier( m_TempDepthId ) );
                cb.SetComputeTextureParam( m_MomentBlurCS, m_KernelEVSM_4[j], "outputTex", m_ShadowmapId );
                cb.SetComputeTextureParam( m_MomentBlurCS, m_KernelMSM[j]   , "depthTex" , new RenderTargetIdentifier( m_TempDepthId ) );
                cb.SetComputeTextureParam( m_MomentBlurCS, m_KernelMSM[j]   , "outputTex", m_ShadowmapId );
            }

            while( i < cnt && m_EntryCache[i].current.slice == rendertargetSlice )
            {
                AdditionalLightData ald = lights[m_EntryCache[i].key.visibleIdx].light.GetComponent<AdditionalLightData>();
                int shadowDataFormat;
                int[] shadowData = ald.GetShadowData( out shadowDataFormat );

                ShadowAlgorithm algo;
                ShadowVariant vari;
                ShadowPrecision prec;
                ShadowUtils.Unpack( m_EntryCache[i].current.shadowAlgo, out algo, out vari, out prec );
                switch( algo )
                {
                case ShadowAlgorithm.VSM:
                    {
                        kernelIdx = shadowData[2];
                        currentKernel = m_KernelVSM[kernelIdx];
                    }
                    break;
                case ShadowAlgorithm.EVSM:
                    {
                        if( vari == ShadowVariant.V0 )
                        {
                            float evsmExponent1 = ShadowUtils.Asfloat( shadowData[2] );
                            cb.SetComputeFloatParam( m_MomentBlurCS, "evsmExponent", evsmExponent1 );
                            kernelIdx = shadowData[4];
                            currentKernel = m_KernelEVSM_2[kernelIdx];
                        }
                        else
                        {
                            float evsmExponent1 = ShadowUtils.Asfloat( shadowData[2] );
                            float evsmExponent2 = ShadowUtils.Asfloat( shadowData[3] );
                            cb.SetComputeFloatParams( m_MomentBlurCS, "evsmExponents", new float[] { evsmExponent1, evsmExponent2 } );
                            kernelIdx = shadowData[4];
                            currentKernel = m_KernelEVSM_4[kernelIdx];
                        }
                    }
                    break;
                case ShadowAlgorithm.MSM :
                    {
                        kernelIdx = shadowData[3];
                        currentKernel = m_KernelMSM[kernelIdx];
                    }
                    break;
                default: Debug.LogError( "Unknown shadow algorithm selected for moment type shadow maps." ); break;
                }
                // TODO: Need a check here whether the shadowmap actually got updated, but right now that's queried on the cullResults.
                Rect r = m_EntryCache[i].current.viewport;
                cb.SetComputeIntParams( m_MomentBlurCS, "srcRect", new int[] { (int) r.x, (int) r.y, (int) r.width, (int) r.height } );
                cb.SetComputeIntParams( m_MomentBlurCS, "dstRect", new int[] { (int) r.x, (int) r.y, (int) rendertargetSlice, (int) m_Flags } );
                cb.SetComputeFloatParams( m_MomentBlurCS, "blurWeightsStorage", m_BlurWeights[kernelIdx] );
                cb.DispatchCompute( m_MomentBlurCS, currentKernel, ((int) r.width) / k_MomentBlurThreadsPerWorkgroup, (int) r.height / k_MomentBlurThreadsPerWorkgroup, 1 );
                i++;
            }
           base.PostUpdate( frameId, cb, rendertargetSlice, lights );
        }
    }
// -------------------------------------------------------------------------------------------------------------------------------------------------
//
//                                                      ShadowManager
//
// -------------------------------------------------------------------------------------------------------------------------------------------------


    // Standard shadow manager
    public class ShadowManager : ShadowManagerBase
    {
        protected class ShadowContextAccess : ShadowContext
        {
            public ShadowContextAccess( ref ShadowContext.CtxtInit initializer ) : base( ref initializer ) { }
            // unfortunately ref returns are only a C# 7.0 feature
            public VectorArray<ShadowData>      shadowDatas  { get { return m_ShadowDatas; } set { m_ShadowDatas = value; } }
            public VectorArray<ShadowPayload>   payloads     { get { return m_Payloads;    } set { m_Payloads = value;    } }
        }

        private const int           k_MaxShadowmapPerType = 4;
        private ShadowSettings      m_ShadowSettings;
        private ShadowmapBase[]     m_Shadowmaps;
        private ShadowmapBase[,]    m_ShadowmapsPerType = new ShadowmapBase[(int)GPUShadowType.MAX, k_MaxShadowmapPerType];
        private ShadowContextAccess m_ShadowCtxt;
        private int[,]              m_MaxShadows    = new int[(int)GPUShadowType.MAX,2];
        // The following vectors are just temporary helpers to avoid reallocation each frame. Contents are not stable.
        private VectorArray<long>   m_TmpSortKeys   = new VectorArray<long>( 0, false );
        private ShadowRequestVector m_TmpRequests   = new ShadowRequestVector( 0, false );
        // The following vector holds data that are returned to the caller so it can be sent to GPU memory in some form. Contents are stable in between calls to ProcessShadowRequests.
        private ShadowIndicesVector m_ShadowIndices = new ShadowIndicesVector( 0, false );

        public ShadowManager( ShadowSettings shadowSettings, ref ShadowContext.CtxtInit ctxtInitializer, ShadowmapBase[] shadowmaps )
        {
            m_ShadowSettings = shadowSettings;
            m_ShadowCtxt = new ShadowContextAccess( ref ctxtInitializer );

            Debug.Assert( shadowmaps != null && shadowmaps.Length > 0 );
            m_Shadowmaps = shadowmaps;
            foreach( var sm in shadowmaps )
            {
                sm.Register( this );
                sm.ReserveSlots( m_ShadowCtxt );
                ShadowmapBase.ShadowSupport smsupport = sm.QueryShadowSupport();
                for( int i = 0, bit = 1; i < (int) GPUShadowType.MAX; ++i, bit <<= 1 )
                {
                    if( ((int)smsupport & bit) == 0 )
                        continue;

                    for( int idx = 0; i < k_MaxShadowmapPerType; ++idx )
                    {
                        if( m_ShadowmapsPerType[i,idx] == null )
                        {
                            m_ShadowmapsPerType[i,idx] = sm;
                            break;
                        }
                    }
                    Debug.Assert( m_ShadowmapsPerType[i,k_MaxShadowmapPerType-1] == null || m_ShadowmapsPerType[i,k_MaxShadowmapPerType-1] == sm,
                        "Only up to " + k_MaxShadowmapPerType + " are allowed per light type. If more are needed then increase ShadowManager.k_MaxShadowmapPerType" );
                }
            }

            m_MaxShadows[(int)GPUShadowType.Point      ,0] = m_MaxShadows[(int)GPUShadowType.Point        ,1] = 4;
            m_MaxShadows[(int)GPUShadowType.Spot       ,0] = m_MaxShadows[(int)GPUShadowType.Spot         ,1] = 8;
            m_MaxShadows[(int)GPUShadowType.Directional,0] = m_MaxShadows[(int)GPUShadowType.Directional  ,1] = 2;

#if UNITY_EDITOR
            // and register itself
            AdditionalLightDataEditor.SetRegistry( this );
#endif
        }

        public override void UpdateCullingParameters( ref CullingParameters cullingParams )
        {
            cullingParams.shadowDistance = Mathf.Min( m_ShadowSettings.maxShadowDistance, cullingParams.shadowDistance );
        }

        public override void ProcessShadowRequests( FrameId frameId, CullResults cullResults, Camera camera, VisibleLight[] lights, ref uint shadowRequestsCount, int[] shadowRequests, out int[] shadowDataIndices )
        {
            shadowDataIndices = null;

            // TODO:
            // Cached the cullResults here so we don't need to pass them around.
            // Allocate needs to pass them to the shadowmaps, as the ShadowUtil functions calculating view/proj matrices need them to call into C++ land.
            // Ideally we can get rid of that at some point, then we wouldn't need to cache them here, anymore.
            foreach( var sm in m_Shadowmaps )
            {
                sm.Assign( cullResults );
            }

            if( shadowRequestsCount == 0 || lights == null || shadowRequests == null )
            {
                shadowRequestsCount = 0;
                return;
            }

            // first sort the shadow casters according to some priority
            PrioritizeShadowCasters( camera, lights, shadowRequestsCount, shadowRequests );

            // next prune them based on some logic
            VectorArray<int> requestedShadows = new VectorArray<int>( shadowRequests, 0, shadowRequestsCount, false );
            m_TmpRequests.Reset( shadowRequestsCount );
            uint totalGranted;
            PruneShadowCasters( camera, lights, ref requestedShadows, ref m_TmpRequests, out totalGranted );

            // if there are no shadow casters at this point -> bail
            if( totalGranted == 0 )
            {
                shadowRequestsCount = 0;
                return;
            }

            // TODO: Now would be a good time to kick off the culling jobs for the granted requests - but there's no way to control that at the moment.

            // finally go over the lights deemed shadow casters and try to fit them into the shadow map
            // shadowmap allocation must succeed at this point.
            m_ShadowCtxt.ClearData();
            ShadowDataVector shadowVector = m_ShadowCtxt.shadowDatas;
            ShadowPayloadVector payloadVector = m_ShadowCtxt.payloads;
            m_ShadowIndices.Reset( m_TmpRequests.Count() );
            AllocateShadows( frameId, lights, totalGranted, ref m_TmpRequests, ref m_ShadowIndices, ref shadowVector, ref payloadVector );
            Debug.Assert( m_TmpRequests.Count() == m_ShadowIndices.Count() );
            m_ShadowCtxt.shadowDatas = shadowVector;
            m_ShadowCtxt.payloads = payloadVector;

            // and set the output parameters
            uint offset;
            shadowDataIndices = m_ShadowIndices.AsArray( out offset, out shadowRequestsCount );
        }

        public class SortReverter : System.Collections.Generic.IComparer<long>
        {
            public int Compare(long lhs, long rhs)
            {
                return rhs.CompareTo(lhs);
            }
        }

        protected override void PrioritizeShadowCasters( Camera camera, VisibleLight[] lights, uint shadowRequestsCount, int[] shadowRequests )
        {
            // this function simply looks at the projected area on the screen, ignoring all light types and shapes
            m_TmpSortKeys.Reset( shadowRequestsCount );

            for( int i = 0; i < shadowRequestsCount; ++i )
            {
                int vlidx = shadowRequests[i];
                VisibleLight vl = lights[vlidx];
                Light l = vl.light;

                // use the screen rect as a measure of importance
                float area = vl.screenRect.width * vl.screenRect.height;
                long val = ShadowUtils.Asint( area );
                val <<= 32;
                val |= (uint)vlidx;
                m_TmpSortKeys.AddUnchecked( val );
            }

            m_TmpSortKeys.Sort( new SortReverter() );
            m_TmpSortKeys.ExtractTo( shadowRequests, 0, out shadowRequestsCount, delegate(long key) { return (int) (key & 0xffffffff); } );
        }

        protected override void PruneShadowCasters( Camera camera, VisibleLight[] lights, ref VectorArray<int> shadowRequests, ref ShadowRequestVector requestsGranted, out uint totalRequestCount )
        {
            Debug.Assert( shadowRequests.Count() > 0 );
            // at this point the array is sorted in order of some importance determined by the prioritize function
            requestsGranted.Reserve( shadowRequests.Count() );
            totalRequestCount = 0;
            Vector3 campos = new Vector3( camera.transform.position.x, camera.transform.position.y, camera.transform.position.z );

            ShadowmapBase.ShadowRequest sreq = new ShadowmapBase.ShadowRequest();
            uint totalSlots = ResetMaxShadows();
            // there's a 1:1 mapping between the index in the shadowRequests array and the element in requestsGranted at the same index.
            // if the prune function skips requests it must make sure that the array is still compact
            m_TmpSortKeys.Reset( shadowRequests.Count() );
            for( uint i = 0, count = shadowRequests.Count(); i < count && totalSlots > 0; ++i )
            {
                int requestIdx        = shadowRequests[i];
                VisibleLight vl       = lights[requestIdx];
                int facecount         = 0;
                GPUShadowType shadowType = GPUShadowType.Point;

                AdditionalLightData ald = vl.light.GetComponent<AdditionalLightData>();
                Vector3 lpos            = vl.light.transform.position;
                float   distToCam       = (campos - lpos).magnitude;
                bool    add             = (distToCam < ald.shadowFadeDistance || vl.lightType == LightType.Directional) && m_ShadowSettings.enabled;

                if( add )
                {
                    switch( vl.lightType )
                    {
                        case LightType.Directional:
                            add = --m_MaxShadows[(int)GPUShadowType.Directional, 0] >= 0;
                            shadowType = GPUShadowType.Directional;
                            facecount = m_ShadowSettings.directionalLightCascadeCount;
                            break;
                        case LightType.Point:
                            add = --m_MaxShadows[(int)GPUShadowType.Point, 0] >= 0;
                            shadowType = GPUShadowType.Point;
                            facecount = 6;
                            break;
                        case LightType.Spot:
                            add = --m_MaxShadows[(int)GPUShadowType.Spot, 0] >= 0;
                            shadowType = GPUShadowType.Spot;
                            facecount = 1;
                            break;
                    }
                }

                if( add )
                {
                    sreq.instanceId = vl.light.GetInstanceID();
                    sreq.index      = (uint) requestIdx;
                    sreq.facemask   = (uint) (1 << facecount) - 1;
                    sreq.shadowType = shadowType;

                    int sa, sv, sp;
                    ald.GetShadowAlgorithm( out sa, out sv, out sp );
                    sreq.shadowAlgorithm = ShadowUtils.Pack( (ShadowAlgorithm) sa, (ShadowVariant) sv, (ShadowPrecision) sp );
                    totalRequestCount += (uint) facecount;
                    requestsGranted.AddUnchecked( sreq );
                    totalSlots--;
                }
                else
                    m_TmpSortKeys.AddUnchecked( requestIdx );
            }
            // make sure that shadowRequests contains all light indices that are going to cast a shadow first, then the rest
            shadowRequests.Reset();
            requestsGranted.ExtractTo( ref shadowRequests, (ShadowmapBase.ShadowRequest request) => { return (int) request.index; } );
            m_TmpSortKeys.ExtractTo( ref shadowRequests, (long idx) => { return (int) idx; } );
        }

        protected override void AllocateShadows( FrameId frameId, VisibleLight[] lights, uint totalGranted, ref ShadowRequestVector grantedRequests, ref ShadowIndicesVector shadowIndices, ref ShadowDataVector shadowDatas, ref ShadowPayloadVector shadowmapPayload )
        {
            ShadowData sd = new ShadowData();
            shadowDatas.Reserve( totalGranted );
            shadowIndices.Reserve( grantedRequests.Count() );
            for( uint i = 0, cnt = grantedRequests.Count(); i < cnt; ++i )
            {
                VisibleLight        vl  = lights[grantedRequests[i].index];
                Light               l   = vl.light;
                AdditionalLightData ald = l.GetComponent<AdditionalLightData>();

                // set light specific values that are not related to the shadowmap
                GPUShadowType shadowtype;
                ShadowUtils.MapLightType( ald.archetype, vl.lightType, out shadowtype );
                // current bias value range is way too large, so scale by 0.01 for now until we've decided  whether to actually keep this value or not.
                sd.bias = 0.01f * (SystemInfo.usesReversedZBuffer ? l.shadowBias : -l.shadowBias);
                sd.normalBias = 100.0f * l.shadowNormalBias;

                shadowIndices.AddUnchecked( (int) shadowDatas.Count() );

                int smidx = 0;
                while( smidx < k_MaxShadowmapPerType )
                {
                    if( m_ShadowmapsPerType[(int)shadowtype,smidx] != null && m_ShadowmapsPerType[(int)shadowtype,smidx].Reserve( frameId, ref sd, grantedRequests[i], (uint) ald.shadowResolution, (uint) ald.shadowResolution, ref shadowDatas, ref shadowmapPayload, lights ) )
                        break;
                    smidx++;
                }
                if( smidx == k_MaxShadowmapPerType )
                    throw new ArgumentException("The requested shadows do not fit into any shadowmap.");
            }

            // final step for shadowmaps that only gather data during the previous loop and do the actual allocation once they have all the data.
            foreach( var sm in m_Shadowmaps )
            {
                if( !sm.ReserveFinalize( frameId, ref shadowDatas, ref shadowmapPayload ) )
                    throw new ArgumentException( "Shadow allocation failed in the ReserveFinalize step." );
            }
        }

        public override void RenderShadows( FrameId frameId, ScriptableRenderContext renderContext, CullResults cullResults, VisibleLight[] lights )
        {
            using (new HDPipeline.Utilities.ProfilingSample("Render Shadows Exp", renderContext))
            {
                foreach( var sm in m_Shadowmaps )
                {
                    sm.Update( frameId, renderContext, cullResults, lights );
                }
            }
        }

        public override void DisplayShadows(ScriptableRenderContext renderContext, Material displayMaterial, int shadowMapIndex, float screenX, float screenY, float screenSizeX, float screenSizeY)
        {
            using (new HDPipeline.Utilities.ProfilingSample("Display Shadows", renderContext))
            {
                // This code is specific to shadow atlas implementation
                MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();

                CommandBuffer debugCB = new CommandBuffer();
                debugCB.name = "Display shadow Overlay";

                if (shadowMapIndex == -1) // Display the Atlas
                {
                    propertyBlock.SetVector("_TextureScaleBias", new Vector4(1.0f, 1.0f, 0.0f, 0.0f));
                }
                else // Display particular index
                {
                    VectorArray<ShadowData> shadowDatas = m_ShadowCtxt.shadowDatas;
                    uint shadowIdx = Math.Min((uint)shadowMapIndex, shadowDatas.Count());
                    propertyBlock.SetVector("_TextureScaleBias", shadowDatas[shadowIdx].scaleOffset);
                }

                debugCB.SetViewport(new Rect(screenX, screenY, screenSizeX, screenSizeY));
                debugCB.DrawProcedural(Matrix4x4.identity, displayMaterial, 0, MeshTopology.Triangles, 3, 1, propertyBlock);

                renderContext.ExecuteCommandBuffer(debugCB);
            }
        }

        public override void SyncData()
        {
            m_ShadowCtxt.SyncData();
        }

        public override void BindResources( ScriptableRenderContext renderContext )
        {
            foreach( var sm in m_Shadowmaps )
            {
                sm.Fill( m_ShadowCtxt );
            }
            CommandBuffer cb = new CommandBuffer(); // <- can we just keep this around or does this have to be newed every frame?
            cb.name = "Bind resources to GPU";
            m_ShadowCtxt.BindResources( cb );
            renderContext.ExecuteCommandBuffer( cb );
            cb.Dispose();
        }

        // resets the shadow slot counters and returns the sum of all slots
        private uint ResetMaxShadows()
        {
            int total = 0;
            for( int i = 0; i < (int) GPUShadowType.MAX; ++i )
            {
                m_MaxShadows[i,0] = m_MaxShadows[i,1];
                total += m_MaxShadows[i,1];
            }
            return total > 0 ? (uint) total : 0;
        }
    }
} // end of namespace UnityEngine.Experimental.ScriptableRenderLoop
