import sys,json,subprocess,os,collections
# dwgcheck.py file.dwg — structure of a DWG as dwgread sees it: space block headers, orphans, layouts, duplicate handles
for p in sys.argv[1:]:
    js=p+'.json'
    subprocess.run([os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'app', 'tools', 'libredwg', 'dwgread.exe'),'-O','json','-o',js,p],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    j=json.load(open(js,encoding='utf8',errors='replace')); os.remove(js)
    O=j['OBJECTS']; T=lambda o:o.get('object') or o.get('entity')
    hs=collections.Counter(o['handle'][2] for o in O if o.get('handle')); dup=[('%X'%k) for k,v in hs.items() if v>1]
    bc=[o for o in O if T(o)=='BLOCK_CONTROL']; bh=[o for o in O if T(o)=='BLOCK_HEADER']
    ctl=bc[0]['handle'][2] if bc else None
    listed=set(e[2] for e in (bc[0].get('entries') or [])) | {bc[0]['model_space'][2], bc[0]['paper_space'][2]} if bc else set()
    orphan=[(o.get('name'),'%X'%o['handle'][2]) for o in bh if o['handle'][2] not in listed or o['ownerhandle'][2]!=ctl]
    sp=[(o.get('name'),'%X'%o['handle'][2],'layout %X'%o['layout'][2],'blk %X'%o['block_entity'][2],'end %X'%o['endblk_entity'][2]) for o in bh if o.get('name','').lower().startswith(('*model_space','*paper_space'))]
    lay=[(o.get('layout_name'),'%X'%o['handle'][2]) for o in O if T(o)=='LAYOUT']
    print(os.path.basename(p),'objects',len(O),'| dup handles',dup,'| block controls',len(bc),'| orphan block headers',orphan,'| layouts',lay); 
    for s in sp: print('   ',s)
