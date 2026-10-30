"""Model/paper space entity counts of a DXF: owner = the 330 outside 102 groups; paper = 67=1 or owner is a *Paper_Space* record.
Usage: python -X utf8 -I tools/spaces.py file.dxf [...]"""
import sys, collections
def analyse(p):
    raw=open(p,'rb').read().replace(b'\r\n',b'\n').split(b'\n')
    sec=None; cur=None; rec=None; in102=False; br={}; out=[]; blkname=None; blocks=collections.Counter()
    def flush():
        if rec is None: return
        t=rec['t']
        if sec==b'TABLES' and t==b'BLOCK_RECORD' and rec.get('h'): br[rec['h']]=rec.get('n',b'')
        if sec==b'ENTITIES' and t not in (b'SECTION',b'ENDSEC',b'SEQEND',b'VERTEX',b'ATTRIB'): out.append((t,rec.get('o'),rec.get('67')))
        if sec==b'BLOCKS' and t not in (b'BLOCK',b'ENDBLK',b'SEQEND',b'VERTEX',b'ATTRIB') and blkname: blocks[blkname]+=1
    for i in range(0,len(raw)-1,2):
        c=raw[i].strip(); v=raw[i+1].strip()
        if c==b'0':
            flush(); rec={'t':v}; in102=False
            if v==b'SECTION': sec=raw[i+3].strip()
            continue
        if c==b'102': in102 = not v.startswith(b'}'); continue
        if in102: continue
        if c==b'5' and 'h' not in rec: rec['h']=v
        if c==b'2' and 'n' not in rec: rec['n']=v
        if c==b'330' and 'o' not in rec: rec['o']=v
        if c==b'67': rec['67']=v
        if sec==b'BLOCKS' and rec['t']==b'BLOCK' and c==b'2': blkname=v
    flush()
    model=paper=other=0; ptypes=collections.Counter()
    for t,o,f in out:
        name=br.get(o,b'').upper()
        if f==b'1' or name.startswith(b'*PAPER_SPACE'): paper+=1; ptypes[t.decode()]+=1
        elif name.startswith(b'*MODEL_SPACE') or o is None: model+=1
        else: other+=1
    pblocks={k.decode():v for k,v in blocks.items() if k.upper().startswith(b'*PAPER_SPACE')}
    return model,paper,other,dict(ptypes),pblocks
if __name__=='__main__':
    for p in sys.argv[1:]:
        m,pp,o,pt,pb=analyse(p); print(f'{p}\tmodel {m}\tpaper(ENTITIES) {pp}\tother-owner {o}\tpaper types {pt}\tpaper blocks {pb}')
