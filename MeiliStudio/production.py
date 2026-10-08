"""Local Meili production service. Python stdlib; credentials never reach project exports."""
import argparse, base64, copy, datetime, http.server, json, mimetypes, os, secrets, threading, time, urllib.request, urllib.error, urllib.parse, uuid
from pathlib import Path

MODELS={'still':('fal-ai/flux-kontext/dev',0.05),'video':('fal-ai/wan/v2.2-a14b/image-to-video/turbo',0.10)}
IDENTITY='Preserve the reference corgi’s red-gold fur, narrow white blaze, expressive dark eyes, large upright ears, short legs and long body. Four paws, believable dog anatomy. Restrained editorial photography, no text or watermark.'
def seed():
    concepts=[('First coast','Meili on Wheels','The board moves before she is ready.','One tiny coast earns a very serious look back.','Will she try the hill or practice turning?', 'Place this same corgi on a small wooden skateboard on a flat San Francisco waterfront path. Replace her sweater with a fitted sage bandana. Low side view, full body and board visible. Overcast daylight, distant bay softly out of focus. She is poised to coast, all four paws on the deck.', 'One continuous low tracking shot. The corgi coasts slowly a few feet on the skateboard, ears forward and body balanced. She turns her head slightly toward camera at the end. Wheels contact pavement; no jumps, no extra legs.'),('The fan inspection','Palace Meili','A palace dog has opinions about the props.','One measured sniff. The fan passes inspection.','A fan dance next, or a palace garden stroll?', 'Place this same corgi in a quiet Tang-inspired palace courtyard, wearing a simple pale peach hanfu-inspired cape fitted to a dog, paws unobstructed. A closed silk folding fan lies on a low stone step in front of her. Warm limestone, jade green details, morning light. Dog-height portrait, clear eyes and visible paws.', 'Locked dog-height camera. The corgi leans forward to sniff the closed fan on the step, pauses, then lifts her eyes toward the camera. A small breeze moves the cape edge. Delicate, believable movement; the fan stays on the step.'),('Off-duty royalty','Everyday Meili','Her royal duties are being postponed.','One slow blink; she settles more comfortably.','Does she get up for a snack or keep her appointment with the rug?', 'Keep the same corgi and her burgundy sweater with the white scalloped collar. Replace the background with a soft cream rug beside a sunlit window, one cushion in muted plum. Remove all text. Her head rests just above her front paws. Quiet candid pet portrait.', 'Locked close-up. The corgi gives one slow blink, tilts an ear toward a faint sound, then lowers her chin onto her front paws with a tiny contented breath. Maintain exact facial markings and sweater. No dramatic camera movement.')]
    return {'version':1,'budget':2.0,'concepts':[dict(id=str(uuid.uuid4()),title=c[0],universe=c[1],hook=c[2],payoff=c[3],vote=c[4],stillPrompt=c[5]+' '+IDENTITY,motionPrompt=c[6],reference='',approvedStill='',collabId='',notes='',decision='Unreviewed',views='',retention='',status='Brief ready') for c in concepts], 'collabs':[], 'jobs':[], 'inspiration':[{'title':'SF waterfront / location','url':'https://commons.wikimedia.org/wiki/Category:Embarcadero_(San_Francisco)','note':'Low eye line, fog, cream pavement. Research reference; not a Meili identity image.'},{'title':'Tang clothing / silhouette','url':'https://commons.wikimedia.org/wiki/Category:Clothing_of_the_Tang_dynasty','note':'Study drape and color. Keep paws free; use a dog-fitted cape rather than human anatomy.'}], 'existingVideo':''}

def atomic(path,data):
    path.parent.mkdir(parents=True,exist_ok=True)
    temp=path.with_suffix('.tmp'); temp.write_text(json.dumps(data,indent=2)); os.chmod(temp,0o600); temp.replace(path)

class Studio:
    def __init__(self,root,assets):
        self.root=Path(root);self.assets=Path(assets);self.root.mkdir(parents=True,exist_ok=True);os.chmod(self.root,0o700)
        self.path=self.root/'production.json';self.lock=threading.RLock();self.token=secrets.token_urlsafe(32)
        self.state=json.loads(self.path.read_text()) if self.path.exists() else seed()
        if not self.path.exists():self.save()
        for j in self.state['jobs']:
            if j['status']=='Submitting':j['status']='Unknown submission';j['error']='Submission interrupted. Check fal history before starting another attempt.'
        self.save()
    def save(self):atomic(self.path,self.state)
    def key(self):
        env=os.environ.get('FAL_KEY') or os.environ.get('FAL_API_KEY')
        path=self.root/'fal-secret.json'
        return env or (json.loads(path.read_text()).get('key','') if path.exists() else '')
    def public(self):
        with self.lock:
            d=copy.deepcopy(self.state);d['reservedToday']=round(sum(j['estimate'] for j in d['jobs'] if j['date']==datetime.date.today().isoformat()),2);d['connected']=bool(self.key());d['models']={k:{'id':v[0],'estimate':v[1]} for k,v in MODELS.items()};return d
    def request(self,url,payload=None):
        if urllib.parse.urlparse(url).hostname!='queue.fal.run':raise ValueError('Unexpected queue host')
        req=urllib.request.Request(url,data=None if payload is None else json.dumps(payload).encode(),headers={'Authorization':'Key '+self.key(),'Content-Type':'application/json'})
        with urllib.request.urlopen(req,timeout=45) as r:return json.load(r)
    def reference(self,value):
        if value.startswith('https://'):return value
        path=(self.assets/value) if value.startswith('seed/') else (self.root/value)
        base=self.assets if value.startswith('seed/') else self.root
        if not path.resolve().is_relative_to(base.resolve()) or not path.is_file():raise ValueError('Attach a valid reference first')
        return 'data:'+ (mimetypes.guess_type(str(path))[0] or 'image/png')+';base64,'+base64.b64encode(path.read_bytes()).decode()
    def submit(self,cid,kind,nonce,quality="draft"):
        if kind not in MODELS or quality not in ['draft','polished']:raise ValueError('Unsupported generation')
        with self.lock:
            if not self.key():raise ValueError('Connect fal in Settings first')
            previous=next((j for j in self.state['jobs'] if j['nonce']==nonce),None)
            if previous:return previous
            c=next(c for c in self.state['concepts'] if c['id']==cid)
            if any(j['conceptId']==cid and j['kind']==kind and j['status'] in ['Submitting','IN_QUEUE','IN_PROGRESS','Unknown submission'] for j in self.state['jobs']):raise ValueError('This shot already has an active or uncertain request')
            if kind=='video' and not c['approvedStill']:raise ValueError('Approve a still before animating')
            amount=0.50 if kind=='video' and quality=='polished' else MODELS[kind][1]
            model='alibaba/wan-3.0/image-to-video' if kind=='video' and quality=='polished' else MODELS[kind][0]
            today=datetime.date.today().isoformat()
            total=sum(j['estimate'] for j in self.state['jobs'] if j['date']==today)
            if total+amount>float(self.state['budget']):raise ValueError('Daily estimate budget reached. Adjust it in Settings.')
            image=self.reference(c['reference'] if kind=='still' else c['approvedStill'])
            prompt=c['stillPrompt'] if kind=='still' else c['motionPrompt']
            if not prompt.strip():raise ValueError('Write a prompt first')
            payload={'image_url':image,'prompt':prompt}
            if kind=='still':payload.update(num_images=1,resolution_mode='9:16',output_format='jpeg')
            else:payload.update(resolution='720p',aspect_ratio='9:16',enable_output_safety_checker=True,enable_prompt_expansion=True,acceleration='regular')
            if kind=='video' and quality=='polished':payload={'start_image_url':image,'prompt':prompt,'duration':5,'resolution':'720p','aspect_ratio':'9:16','audio':False,'enable_prompt_expansion':True}
            j=dict(id=str(uuid.uuid4()),nonce=nonce,conceptId=cid,title=c['title'],kind=kind,model=model,quality=quality,estimate=amount,date=today,status='Submitting',prompt=prompt,reference=c['reference'] if kind=='still' else c['approvedStill'],created=time.time(),error='')
            self.state['jobs'].append(j);self.save()
        try:
            answer=self.request('https://queue.fal.run/'+j['model'],payload)
            if not answer.get('request_id'):raise ValueError('No request ID; check fal history')
            with self.lock:
                for k in ['request_id','status_url','response_url']:j[k]=answer.get(k,'')
                j['status']='IN_QUEUE';self.save()
        except urllib.error.HTTPError as e:
            with self.lock:j['status']='Failed';j['error']='fal rejected the request (HTTP %s). Check your key, balance and model access.'%e.code;self.save()
        except Exception:
            with self.lock:j['status']='Unknown submission';j['error']='The submission outcome is uncertain. Check fal history; this app will not automatically resubmit.';self.save()
        return j
    def poll(self):
        while True:
            time.sleep(10)
            with self.lock:jobs=[j for j in self.state['jobs'] if j['status'] in ['IN_QUEUE','IN_PROGRESS','COMPLETED']]
            if not self.key():continue
            for j in jobs:
                try:
                    answer=self.request(j['status_url'])
                    with self.lock:j['status']=answer['status'];j['checked']=time.time();self.save()
                    if answer['status']=='COMPLETED':
                        if answer.get('error'):
                            with self.lock:j.update(status='Failed',error=str(answer['error'])[:500]);self.save()
                            continue
                        result=self.request(j['response_url'])
                        if result.get('error'):
                            with self.lock:j.update(status='Failed',error=str(result['error'])[:500]);self.save()
                            continue
                        url=result['images'][0]['url'] if j['kind']=='still' else result['video']['url']
                        if urllib.parse.urlparse(url).scheme!='https':raise ValueError('Invalid result URL')
                        suffix='.jpg' if j['kind']=='still' else '.mp4';relative='results/'+j['id']+suffix;dest=self.root/relative;dest.parent.mkdir(exist_ok=True)
                        # Output CDN fetches intentionally carry no fal credential.
                        with urllib.request.urlopen(url,timeout=90) as r:dest.write_bytes(r.read())
                        with self.lock:j.update(status='Ready',result=relative,resultUrl=url,error='');self.save()
                        atomic(dest.with_suffix('.json'),{k:v for k,v in j.items() if k not in ['status_url','response_url']})
                except urllib.error.HTTPError as e:
                    with self.lock:
                        if e.code in [400,401,403,404,422]:j['status']='Failed'
                        j['error']='Status/result check returned HTTP %s; check fal history.'%e.code;self.save()
                except Exception as e:
                    with self.lock:
                        # Completed jobs whose download fails can be recovered by polling again.
                        j['status']='IN_PROGRESS' if j['status']=='COMPLETED' else j['status'];j['error']='Could not fetch result; will check again.';self.save()

class Handler(http.server.BaseHTTPRequestHandler):
    def log_message(self,*args):pass
    @property
    def studio(self):return self.server.studio
    def output(self,data,status=200):
        raw=json.dumps(data).encode();self.send_response(status);self.send_header('Content-Type','application/json');self.send_header('Cache-Control','no-store');self.end_headers();self.wfile.write(raw)
    def authorized(self):return secrets.compare_digest(self.headers.get('Cookie','').split('meili=')[-1].split(';')[0],self.studio.token)
    def do_GET(self):
        p=urllib.parse.urlparse(self.path)
        if p.path=='/boot' and secrets.compare_digest(urllib.parse.parse_qs(p.query).get('token',[''])[0],self.studio.token):
            self.send_response(302);self.send_header('Set-Cookie','meili='+self.studio.token+'; HttpOnly; SameSite=Strict; Path=/');self.send_header('Location','/workflow.html');self.end_headers();return
        if not self.authorized():return self.output({'error':'Unauthorized'},403)
        if p.path=='/api/state':return self.output(self.studio.public())
        if p.path=='/api/export':return self.output(self.studio.public())
        root=self.studio.root if p.path.startswith('/media/') else self.studio.assets
        rel=p.path[len('/media/'):] if p.path.startswith('/media/') else p.path.lstrip('/')
        path=(root/urllib.parse.unquote(rel)).resolve()
        if not path.is_relative_to(root.resolve()) or not path.is_file() or path.suffix not in ['.html','.js','.css','.png','.jpg','.jpeg','.webp','.mp4','.json'] or path.name=='fal-secret.json':return self.output({'error':'Not found'},404)
        data=path.read_bytes();self.send_response(200);self.send_header('Content-Type',mimetypes.guess_type(str(path))[0] or 'application/octet-stream');self.send_header('Content-Length',str(len(data)));self.send_header('Cache-Control','no-store');self.end_headers();self.wfile.write(data)
    def do_POST(self):
        if not self.authorized() or self.headers.get('Origin')!='http://'+self.headers.get('Host',''):return self.output({'error':'Unauthorized origin'},403)
        try:
            length=int(self.headers.get('Content-Length',0))
            if length>12000000:raise ValueError('File too large (8 MB maximum)')
            d=json.loads(self.rfile.read(length));action=urllib.parse.urlparse(self.path).path
            with self.studio.lock:
                if action=='/api/key':
                    key=d['key'].strip()
                    if not key or len(key)>500:raise ValueError('Enter a valid fal key')
                    atomic(self.studio.root/'fal-secret.json',{'key':key});return self.output({'ok':True})
                if action=='/api/settings':
                    budget=float(d['budget'])
                    if not 0<budget<=100:raise ValueError('Daily budget must be between $0 and $100')
                    self.studio.state['budget']=budget;self.studio.save();return self.output({'ok':True})
                if action=='/api/concept':
                    c=next(c for c in self.studio.state['concepts'] if c['id']==d['id'])
                    for k in ['title','universe','hook','payoff','vote','stillPrompt','motionPrompt','collabId','notes','decision','views','retention']:
                        if k in d:
                            if not isinstance(d[k],str) or len(d[k])>20000:raise ValueError('Invalid field')
                            if k=='stillPrompt' and d[k]!=c[k]:c['approvedStill']=''
                            c[k]=d[k]
                    self.studio.save();return self.output({'ok':True})
                if action=='/api/new':
                    c=copy.deepcopy(seed()['concepts'][0]);c.update(id=str(uuid.uuid4()),title='Untitled episode',hook='',payoff='',stillPrompt='',motionPrompt='',approvedStill='');self.studio.state['concepts'].append(c);self.studio.save();return self.output(c)
                if action=='/api/upload':
                    ext=Path(d['name']).suffix.lower()
                    if ext not in ['.png','.jpg','.jpeg','.webp']:raise ValueError('Use PNG, JPEG or WebP')
                    raw=base64.b64decode(d['data'].split(',')[-1],validate=True)
                    if len(raw)>8000000:raise ValueError('Use an image smaller than 8 MB')
                    valid=raw.startswith(b'\x89PNG\r\n\x1a\n') or raw.startswith(b'\xff\xd8\xff') or (raw[:4]==b'RIFF' and raw[8:12]==b'WEBP')
                    if not valid:raise ValueError('Invalid image')
                    rel='references/'+str(uuid.uuid4())+ext;path=self.studio.root/rel;path.parent.mkdir(exist_ok=True);path.write_bytes(raw)
                    c=next(c for c in self.studio.state['concepts'] if c['id']==d['id']);c['reference']=rel;c['approvedStill']='';self.studio.save();return self.output({'ok':True})
                if action=='/api/approve':
                    c=next(c for c in self.studio.state['concepts'] if c['id']==d['id']);j=next(j for j in self.studio.state['jobs'] if j['id']==d['jobId'] and j['conceptId']==c['id'] and j['kind']=='still' and j['status']=='Ready');c['approvedStill']=j['result'];self.studio.save();return self.output({'ok':True})
                if action=='/api/collab':
                    cols=self.studio.state['collabs'];c=next((x for x in cols if x['id']==d.get('id')),None)
                    if c is None:c={'id':str(uuid.uuid4())};cols.append(c)
                    for k in ['name','category','fit','integration','status','link','notes']:
                        value=d.get(k,'')
                        if not isinstance(value,str) or len(value)>10000:raise ValueError('Invalid collaborator')
                        c[k]=value
                    self.studio.save();return self.output({'ok':True})
            if action=='/api/generate':return self.output(self.studio.submit(d['id'],d['kind'],d['nonce'],d.get('quality','draft')))
            return self.output({'error':'Unknown action'},404)
        except (ValueError,KeyError,StopIteration) as e:self.output({'error':str(e) or 'Invalid request'},400)
        except Exception:self.output({'error':'Could not save or process this request. Your existing work is preserved.'},500)

def main():
    a=argparse.ArgumentParser();a.add_argument('--directory',required=True);a.add_argument('--assets',required=True);args=a.parse_args()
    studio=Studio(args.directory,args.assets);server=http.server.ThreadingHTTPServer(('127.0.0.1',0),Handler);server.studio=studio
    threading.Thread(target=studio.poll,daemon=True).start()
    print('http://127.0.0.1:'+str(server.server_port)+'/boot?token='+studio.token,flush=True)
    server.serve_forever()
if __name__=='__main__':main()
