"""Execute original ARM64 AI numeric routines and emit a portable oracle.

Uses task-local unicorn plus the already available pyelftools. It never loads
the APK in a process, starts Unity, or mutates the shared Ghidra project.
Unity Random.Range and libm are bounded hooks; AI/curve instructions run intact.
"""
import json
import math
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "output/bloody-full-port-20261028/ai-check"
sys.path.insert(0, str(OUT / "vendor"))
from unicorn import Uc, UC_ARCH_ARM64, UC_MODE_ARM, UC_HOOK_CODE
from unicorn.arm64_const import (
    UC_ARM64_REG_SP, UC_ARM64_REG_LR, UC_ARM64_REG_PC,
    UC_ARM64_REG_X0, UC_ARM64_REG_X1, UC_ARM64_REG_X2, UC_ARM64_REG_X3, UC_ARM64_REG_X8,
    UC_ARM64_REG_S0, UC_ARM64_REG_S1, UC_ARM64_REG_S2, UC_ARM64_REG_S3, UC_ARM64_REG_D0,
)
from elftools.elf.elffile import ELFFile


def f32(value):
    return struct.unpack("<f", struct.pack("<f", value))[0]


class NativeAI:
    SELF = 0x10011000
    PROFILE = 0x10012000
    RETURN = 0x10001000

    def __init__(self):
        self.cpu = Uc(UC_ARCH_ARM64, UC_MODE_ARM)
        self.cpu.mem_map(0, 0x7900000)
        with (ROOT / "output/bloody-reference-20261028/libil2cpp.so").open("rb") as stream:
            elf = ELFFile(stream)
            for segment in elf.iter_segments():
                if segment["p_type"] == "PT_LOAD":
                    self.cpu.mem_write(segment["p_vaddr"], segment.data())
        self.cpu.mem_map(0x10000000, 0x20000)
        # Initialized IL2CPP type cells for Mathf and cv. Their pure routines
        # need no objects, Unity transforms, or other managed runtime state.
        pointer, klass, statics = 0x10015000, 0x10015100, 0x10015300
        self.cpu.mem_write(pointer, struct.pack("<Q", klass))
        self.cpu.mem_write(klass + 0xE4, struct.pack("<I", 1))
        self.cpu.mem_write(klass + 0xB8, struct.pack("<Q", statics))
        for address in (0x720D160, 0x72126E0):
            self.cpu.mem_write(address, struct.pack("<Q", pointer))
        self.random_fraction = .5
        self.random_calls = []
        self.cpu.hook_add(UC_HOOK_CODE, self._hook)
        self.call(0x3514EE4)

    def sf(self, index):
        return struct.unpack("<f", struct.pack("<I", self.cpu.reg_read(UC_ARM64_REG_S0 + index)))[0]

    def setf(self, index, value):
        self.cpu.reg_write(UC_ARM64_REG_S0 + index, struct.unpack("<I", struct.pack("<f", value))[0])

    def _hook(self, cpu, address, size, data):
        if address in (0x316B828, 0x316BA24):
            pass
        elif address == 0x6E2BD90:
            self.setf(0, math.atan2(self.sf(0), self.sf(1)))
        elif address == 0x6E2BDD0:
            angle = self.sf(0)
            cpu.mem_write(cpu.reg_read(UC_ARM64_REG_X0), struct.pack("<f", math.sin(angle)))
            cpu.mem_write(cpu.reg_read(UC_ARM64_REG_X1), struct.pack("<f", math.cos(angle)))
        elif address == 0x6ABFA9C:
            lo, hi = self.sf(0), self.sf(1)
            self.random_calls.append([lo, hi])
            self.setf(0, lo + (hi - lo) * self.random_fraction)
        elif address == 0x6E2BD40:
            value=struct.unpack('<d',struct.pack('<Q',cpu.reg_read(UC_ARM64_REG_D0)))[0]
            fraction,integer=math.modf(value)
            cpu.mem_write(cpu.reg_read(UC_ARM64_REG_X0),struct.pack('<d',integer))
            cpu.reg_write(UC_ARM64_REG_D0,struct.unpack('<Q',struct.pack('<d',fraction))[0])
        else:
            return
        cpu.reg_write(UC_ARM64_REG_PC, cpu.reg_read(UC_ARM64_REG_LR))

    def call(self, address, x0=0, x1=0, x8=0, floats=()):
        cpu = self.cpu
        cpu.reg_write(UC_ARM64_REG_SP, 0x10010000)
        cpu.reg_write(UC_ARM64_REG_LR, self.RETURN)
        for reg, value in ((UC_ARM64_REG_X0,x0),(UC_ARM64_REG_X1,x1),(UC_ARM64_REG_X8,x8)):
            cpu.reg_write(reg,value)
        for index, value in enumerate(floats):
            self.setf(index,value)
        cpu.emu_start(address,self.RETURN,count=100000)
        if cpu.reg_read(UC_ARM64_REG_PC) != self.RETURN:
            raise RuntimeError(f"Native routine failed to return: {address:x}")

    def profile(self, kind, two):
        self.call(0x3515E48,kind,int(two),self.PROFILE)
        return list(struct.unpack("<11f?", self.cpu.mem_read(self.PROFILE,45)))

    def evaluate(self, values, t, rest, aim):
        self.cpu.mem_write(self.PROFILE, struct.pack("<11f?", *values))
        self.call(0x35142C8,self.PROFILE,floats=(t,*rest,aim))
        return [self.sf(0),self.sf(1)]

    def evaluate2h(self, values, t, aim, dominant, radius, angle):
        self.cpu.mem_write(self.PROFILE, struct.pack("<11f?", *values))
        left,right=self.SELF+0x400,self.SELF+0x408
        self.cpu.reg_write(UC_ARM64_REG_X2,right)
        self.cpu.reg_write(UC_ARM64_REG_X3,int(dominant))
        self.call(0x3515520,self.PROFILE,left,floats=(t,aim,radius,angle))
        return list(struct.unpack("<4f",self.cpu.mem_read(left,16)))

    def pulse(self, sequence, clip, a, b):
        dest=self.SELF+0x500
        self.cpu.mem_write(self.SELF+0x48,struct.pack('<Q',dest))
        self.cpu.mem_write(self.SELF+0xE0,bytes([(sequence-1)&255]))
        self.call(0x3430310,self.SELF,clip,floats=(a,b))
        return struct.unpack('<I',self.cpu.mem_read(dest,4))[0]


def main():
    native = NativeAI()
    profiles, curves, twohand, varied, scalars = [], [], [], [], []
    for kind in range(3):
        for two in (False,True):
            profile = native.profile(kind,two)
            profiles.append(dict(kind=kind,two=two,values=profile))
            for t in (-.1,0,.09,.18,.3,.4,.6,.66,.687,.8,.95,1,1.1):
                for rest in ((.425,.55),(.39,.7),(.07,.7),(0,0)):
                    for aim in (-16,0,16):
                        curves.append(dict(profile=profile,t=t,rest=rest,aim=aim,expected=native.evaluate(profile,t,rest,aim)))
            for t in (0,.3,.7,1):
                for aim in (-16,0,16):
                    for dominant in (False,True):
                        twohand.append(dict(profile=profile,t=t,aim=aim,dominant=dominant,radius=.55,angle=23,expected=native.evaluate2h(profile,t,aim,dominant,.55,23)))
            for amount in (0,.5,1):
                for fraction in (.1,.5,.9):
                    native.random_fraction=fraction;native.random_calls=[]
                    native.profile(kind,two)
                    native.call(0x3515F40,native.PROFILE,x8=native.SELF,floats=(amount,))
                    result=list(struct.unpack("<11f?",native.cpu.mem_read(native.SELF,45)))
                    varied.append(dict(kind=kind,two=two,amount=amount,fraction=fraction,expected=result,randomCalls=native.random_calls))
    for aggression in (0,.25,.5,.75,1):
        native.cpu.mem_write(native.SELF+0x180,struct.pack("<f",aggression))
        native.call(0x342BE5C,native.SELF);stamina=native.sf(0)
        native.call(0x342BE98,native.SELF);abort=native.sf(0)
        native.cpu.mem_write(native.SELF+0x48,b"\1")
        native.random_calls=[];native.call(0x342BDCC,native.SELF)
        scalars.append(dict(aggression=aggression,stamina=stamina,abort=abort,reactionBounds=native.random_calls[-1]))
    pulses=[]
    values=(-.1,0,.5/63,1.5/63,.5,.99,1,1.2)
    for sequence in (0,1,255):
        for clip in range(1,6):
            for a in values:
                for b in values:pulses.append(dict(sequence=sequence,clip=clip,a=a,b=b,expected=native.pulse(sequence,clip,a,b)))
    report=dict(apk_sha256="c9e4a52874f18c58363d976194227d68a699173d72925d22da34a75d0993f2ba",oracle="Original ARM64 instructions under Unicorn; Random.Range and libm hooks",profiles=profiles,curves=curves,twohand=twohand,varied=varied,scalars=scalars,pulses=pulses)
    OUT.mkdir(parents=True,exist_ok=True)
    (OUT/"native-ai-oracle.json").write_text(json.dumps(report,indent=2)+"\n",encoding="utf-8")
    print(json.dumps({"native_default_profiles":len(profiles),"native_curve_cases":len(curves),"native_twohand_cases":len(twohand),"native_varied_profiles":len(varied),"native_scalar_cases":len(scalars),"native_expression_pulses":len(pulses)}))


if __name__ == "__main__":
    main()
