"""Converts FBX exports of the Toon Soldiers pack skeletal meshes into packdata/<name>.json (positions in
the generator space, normals, UVs, triangles, skin weights by bone name). The pack is licensed: packdata/
is git-ignored and never committed.  usage: python pack2json.py <folder of .fbx> <packdata folder>"""
import fbxread as F, numpy as np, json, os, sys, glob
def conv(path):
    r=F.read(path); objs=r.get('Objects')
    byid={o.props[0]:o for o in objs.children}
    conns=[(c.props[1],c.props[2]) for c in r.get('Connections').children if c.props[0]=='OO']
    name=lambda o:o.props[1].split('\x00')[0].replace(' ','-')
    parts=[]
    for g in objs.all('Geometry'):
        v=g.get('Vertices').props[0].reshape(-1,3)
        pvi=g.get('PolygonVertexIndex').props[0]
        tris=[];poly=[]
        for i in pvi:
            if i<0: poly.append(~i); 
            else: poly.append(i); continue
            for k in range(1,len(poly)-1): tris.append((poly[0],poly[k],poly[k+1]))
            poly=[]
        npoly=sum(1 for i in pvi if i<0)
        def layer(nm,key):
            le=g.get(nm)
            if not le: return None
            mp=le.get('MappingInformationType').props[0]; rf=le.get('ReferenceInformationType').props[0]
            assert mp=='ByVertice' and rf=='Direct', (nm,mp,rf)
            return le.get(key).props[0]
        nrm=layer('LayerElementNormal','Normals').reshape(-1,3)
        uv=layer('LayerElementUV','UV').reshape(-1,2)
        col=layer('LayerElementColor','Colors')
        mats=g.get('LayerElementMaterial').get('Materials').props[0]
        # polygon -> tris material
        tm=[];pi=0;cnt=0;poly=[]
        for i in pvi:
            cnt+=1
            if i<0:
                tm+= [int(mats[pi] if len(mats)>1 else mats[0])]*(cnt-2); pi+=1; cnt=0
        weights=[[] for _ in range(len(v))]
        geomid=g.props[0]
        skin=[c for c,p in conns if p==geomid and byid.get(c) is not None and byid[c].name=='Deformer']
        for sk in skin:
            for cid,p in conns:
                if p!=sk: continue
                cl=byid[cid]
                bone=[name(byid[c]) for c,pp in conns if pp==cid and byid[c].name=='Model'][0]
                idx=cl.get('Indexes'); 
                if idx is None: continue
                for i,w in zip(idx.props[0],cl.get('Weights').props[0]):
                    if w>0: weights[i].append([bone,round(float(w),5)])
        meshmodel=[p for c,p in conns if c==geomid][0]
        matnames=[name(byid[c]) for c,p in conns if p==meshmodel and byid[c].name=='Material']
        parts.append(dict(name=name(byid[meshmodel]),materials=matnames,
            verts=np.round(v,4).tolist(),normals=np.round(nrm,5).tolist(),uvs=np.round(uv,6).tolist(),
            colors=(np.round(col.reshape(-1,4),4).tolist() if col is not None else None),
            tris=[list(map(int,t)) for t in tris],triMat=tm,weights=weights))
    return parts
if __name__=='__main__':
    src,dst=sys.argv[1],sys.argv[2]
    for f in sorted(glob.glob(src+'/*.fbx')):
        n=os.path.splitext(os.path.basename(f))[0]
        p=conv(f)
        json.dump(p,open(f'{dst}/{n}.json','w'))
        print(n,[(m['name'],len(m['verts']),len(m['tris']),m['materials'],sorted({b for w in m['weights'] for b,_ in w})[:3]) for m in p])
