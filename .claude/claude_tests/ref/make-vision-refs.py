# DEV MACHINE ONLY. Vision references for the vision gate (GemmaGate vision part):
#  1. images\photo0_resized_ref.png: Pillow bicubic resize of photo0_orig.png to the Gemma 4 280-token size (1056x576),
#     to check RgbImage.Resize byte for byte.
#  2. outputs\<model>\vision-logprobs.tsv: llama.cpp (llama-server --mmproj, CPU) greedy reply to an image prompt, with the
#     log-probability llama.cpp gives each reply token, for teacher-forced comparison.
# usage: python make-vision-refs.py <model.gguf> <mmproj.gguf> [port]
import base64, json, os, subprocess, sys, time, urllib.request, urllib.error

here = os.path.dirname(os.path.abspath(__file__))
img_dir = os.path.join(here, 'images')

from PIL import Image
src = Image.open(os.path.join(img_dir, 'photo0_orig.png')).convert('RGB')
src.resize((1056, 576), Image.BICUBIC).save(os.path.join(img_dir, 'photo0_resized_ref.png'))

if len(sys.argv) < 3:
    sys.exit(0)
model, mmproj = sys.argv[1], sys.argv[2]
port = int(sys.argv[3]) if len(sys.argv) > 3 else 8091
base = 'http://127.0.0.1:%d' % port
question = 'Describe this image in one sentence.'
out_dir = os.path.join(here, 'outputs', os.path.splitext(os.path.basename(model))[0])
os.makedirs(out_dir, exist_ok=True)

def post(path, body):
    req = urllib.request.Request(base + path, json.dumps(body).encode(), {'Content-Type': 'application/json'})
    try:
        return json.load(urllib.request.urlopen(req, timeout=3600))
    except urllib.error.HTTPError as e:
        raise SystemExit('server said: ' + e.read().decode(errors='replace'))

srv = subprocess.Popen(['llama-server', '-m', model, '--mmproj', mmproj, '--no-mmproj-offload', '-c', '4096', '-ngl', '0',
                        '-fa', 'off', '--host', '127.0.0.1', '--port', str(port), '-np', '1', '--image-max-tokens', '1120'],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
try:
    for _ in range(600):
        try:
            urllib.request.urlopen(base + '/health', timeout=2); break
        except Exception:
            time.sleep(0.5)
    img = base64.b64encode(open(os.path.join(img_dir, 'photo0_960x672.png'), 'rb').read()).decode()
    props = json.load(urllib.request.urlopen(base + '/props', timeout=30))
    marker = props.get('media_marker') or '<__media__>'
    print('media marker:', marker)
    prompt = '<|turn>user\n' + marker + question + '<turn|>\n<|turn>model\n<|channel>thought\n<channel|>'
    r = post('/completion', {'prompt': {'prompt_string': prompt, 'multimodal_data': [img]}, 'n_predict': 48, 'temperature': 0,
                             'n_probs': 20, 'return_tokens': True, 'cache_prompt': False})
    lines = ['# question\t' + question, '# reply\t' + r['content'].replace('\n', '\\n'), '# pos\ttoken\tlogprob\ttop1']
    for i, p in enumerate(r['completion_probabilities']):
        tok = p['id']
        lp = next((t['logprob'] for t in p['top_logprobs'] if t['id'] == tok), float('nan'))
        lines.append('%d\t%d\t%r\t%d' % (i, tok, lp, p['top_logprobs'][0]['id']))
    open(os.path.join(out_dir, 'vision-logprobs.tsv'), 'w', encoding='utf-8').write('\n'.join(lines) + '\n')
    print('reply:', r['content'])
finally:
    srv.kill()
