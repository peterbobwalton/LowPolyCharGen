"""Minimal binary FBX reader (node tree, properties, zlib arrays) used by pack2json.py."""
import struct, zlib, sys, numpy as np
class Node:
    __slots__=('name','props','children')
    def __init__(s,n,p,c): s.name,s.props,s.children=n,p,c
    def get(s,name):
        for c in s.children:
            if c.name==name: return c
    def all(s,name): return [c for c in s.children if c.name==name]
    def __repr__(s): return f'{s.name}{s.props[:3]}'
ARR={'f':('f',4),'d':('d',8),'l':('q',8),'i':('i',4),'b':('?',1)}
def read(path):
    data=open(path,'rb').read()
    assert data[:21]==b'Kaydara FBX Binary  \x00'
    ver=struct.unpack_from('<I',data,23)[0]
    big=ver>=7500
    def node(pos):
        if big: end,nprop,plen=struct.unpack_from('<QQQ',data,pos); pos+=24
        else: end,nprop,plen=struct.unpack_from('<III',data,pos); pos+=12
        nl=data[pos]; pos+=1
        if end==0: return None,pos
        name=data[pos:pos+nl].decode('latin1'); pos+=nl
        props=[]
        for _ in range(nprop):
            t=chr(data[pos]); pos+=1
            if t=='Y': props.append(struct.unpack_from('<h',data,pos)[0]); pos+=2
            elif t=='C': props.append(bool(data[pos])); pos+=1
            elif t=='I': props.append(struct.unpack_from('<i',data,pos)[0]); pos+=4
            elif t=='F': props.append(struct.unpack_from('<f',data,pos)[0]); pos+=4
            elif t=='D': props.append(struct.unpack_from('<d',data,pos)[0]); pos+=8
            elif t=='L': props.append(struct.unpack_from('<q',data,pos)[0]); pos+=8
            elif t in 'SR':
                l=struct.unpack_from('<I',data,pos)[0]; pos+=4; b=data[pos:pos+l]; pos+=l
                props.append(b.decode('latin1') if t=='S' else b)
            elif t in ARR:
                n,enc,cl=struct.unpack_from('<III',data,pos); pos+=12
                raw=data[pos:pos+cl]; pos+=cl
                if enc: raw=zlib.decompress(raw)
                fmt,sz=ARR[t]; props.append(np.frombuffer(raw,dtype='<'+fmt,count=n).copy())
            else: raise ValueError(t)
        children=[]
        while pos<end:
            c,pos2=node(pos)
            if c is None: pos=pos2; break
            children.append(c); pos=pos2
        return Node(name,props,children),end
    pos=27; top=[]
    while True:
        n,pos=node(pos)
        if n is None: break
        top.append(n)
    return Node('root',[],top)
def p70(n):
    d={}
    p=n.get('Properties70')
    if p:
        for c in p.children: d[c.props[0]]=c.props[4:]
    return d
