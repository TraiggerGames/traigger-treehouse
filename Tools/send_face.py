"""Prueba UDP local. Ctrl+C termina. No necesita instalar dependencias."""
import argparse,json,math,socket,time
p=argparse.ArgumentParser();p.add_argument('--host',default='127.0.0.1');p.add_argument('--port',type=int,default=49983);a=p.parse_args()
s=socket.socket(socket.AF_INET,socket.SOCK_DGRAM)
try:
    for n in range(1800):
        t=n/30
        frame=dict(version=1,sequence=n,headRoll=math.sin(t)*15,jawOpen=(math.sin(t*7)+1)*.4,eyeBlinkLeft=max(0,1-abs(t%4-.15)/.12),eyeBlinkRight=0,eyeLookX=math.sin(t*.4))
        s.sendto(json.dumps(frame).encode(),(a.host,a.port));time.sleep(1/30)
except KeyboardInterrupt: pass
finally:s.close()
