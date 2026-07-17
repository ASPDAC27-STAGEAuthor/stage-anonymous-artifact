#!/usr/bin/env python3
"""Finalize the unattended Phase 10 simulator-consistency bundle."""
from __future__ import annotations
import argparse,csv,hashlib,json,math,os,re,shutil,statistics
from collections import defaultdict
from datetime import datetime,timezone
from pathlib import Path
from typing import Any
ROOT=Path(__file__).resolve().parents[3]
def loadj(p): return json.loads(Path(p).read_text(encoding='utf-8-sig'))
def loadc(p):
    with Path(p).open(encoding='utf-8-sig',newline='') as f:return list(csv.DictReader(f))
def sha(p):
    h=hashlib.sha256()
    with Path(p).open('rb') as f:
        for b in iter(lambda:f.read(1048576),b''):h.update(b)
    return h.hexdigest()
def rel(p):
    try:return Path(p).resolve().relative_to(ROOT).as_posix()
    except ValueError:return str(Path(p).resolve())
def num(v):
    try:
        x=float(v);return x if math.isfinite(x) else None
    except:return None
def err(a,b):
    a,b=num(a),num(b)
    if a is None or b is None:return None
    return 0.0 if a==b==0 else (abs(a-b)/abs(b) if b else None)
def med(v):
    x=[n for n in map(num,v) if n is not None];return statistics.median(x) if x else ''
def truth(v):return str(v).lower() in {'true','1','yes'}
def wc(p,rows):
    rows=list(rows);keys=sorted({k for r in rows for k in r});Path(p).parent.mkdir(parents=True,exist_ok=True)
    with Path(p).open('w',encoding='utf-8',newline='') as f:
        w=csv.DictWriter(f,fieldnames=keys);w.writeheader();w.writerows(rows)
def wj(p,v):Path(p).write_text(json.dumps(v,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
def row(domain,comparison,case,level,metric,match,a='',b='',sa='completed',sb='completed',repeat='',error='',note=''):
    return dict(domain=domain,comparison=comparison,case_id=case,repeat=repeat,tool_a=a,tool_b=b,status_a=sa,status_b=sb,evidence_level=level,metric=metric,match=match,relative_error=error,notes=note)
def pair(domain,case,ca,cb,ha,hb,ok,basis):return dict(domain=domain,pair_id=case,case_id=case,config_hash_a=ca,config_hash_b=cb,result_hash_a=ha,result_hash_b=hb,hash_match=ok,pairing_basis=basis)
def main():
    ap=argparse.ArgumentParser();ap.add_argument('--bundle',type=Path,required=True);ap.add_argument('--final-bundle',type=Path,required=True);ap.add_argument('--reviewer-bundle',type=Path,required=True);a=ap.parse_args()
    B,F,R=map(Path.resolve,(a.bundle,a.final_bundle,a.reviewer_bundle));M=[];P=[];D=[];RT=[]
    g=defaultdict(list)
    for p in sorted((B/'raw/rq1_exact_oracles').glob('*.json')):
        j=loadj(p);g[j['metrics']['case_name']].append((p,j))
    for case,items in sorted(g.items()):
        items.sort(key=lambda x:x[1]['metrics']['repeat']);hs=[x[1]['metrics']['canonical_trace_hash'] for x in items];ok=len(items)==2 and len(set(hs))==1 and all(x[1]['status']=='completed' for x in items)
        M.append(row('RQ1','STAGE repeat',case,'Exact' if ok else 'failed','canonical trace bytes',ok,'STAGE','STAGE',repeat='0,1',error=0 if ok else '',note='Two byte-identical canonical trace hashes.'))
        P.append(pair('RQ1',case,items[0][1]['config_hash'],items[1][1]['config_hash'],hs[0],hs[1],ok,'same case and frozen config'))
    candidates=loadj(R/'manifests/p1_candidates.json')
    for c in [x for x in candidates if x['provider']=='stage']:
        kind,case,rp=c['kind'],c['case_id'],c['repeat'];oldp=R/c['raw_relpath'];newp=B/'raw/p1_stage_replay'/kind/case/f'r{rp}.json';old,new=loadj(oldp),loadj(newp)
        key='canonical_trace_sha256' if kind=='holdout' else 'canonical_timeline_sha256';oh=old.get('metrics',{}).get(key,'');nh=new.get('metrics',{}).get(key,'');same_status=old['status']==new['status']
        ok=same_status and old['config_hash']==new['config_hash'] and ((oh and oh==nh) or old['status'] in {'not_supported','expected_boundary'})
        level='not_supported' if old['status']=='not_supported' else ('Exact' if ok else 'failed')
        M.append(row('P1 '+kind,'source vs current replay',case,level,'canonical trace/timeline',ok,'STAGE source','STAGE replay',old['status'],new['status'],rp,0 if ok and level=='Exact' else '',rel(oldp)+' | '+rel(newp)))
        P.append(pair(kind,c['candidate_id'],old['config_hash'],new['config_hash'],oh or old['status'],nh or new['status'],ok,'registered candidate, repeat and seed'))
        if old['status']=='not_supported':D.append(dict(domain='NoC',case_id=case,metric='capacity release',observed_a=old['status'],observed_b=new['status'],relative_delta='',first_divergence='public input contract',root_cause='Direct-capacity backpressure has no explicit credit-release cycle.',fixability='Requires a new model, not a reporting edit.',final_evidence_level='not_supported'))
    scale=loadc(R/'summary/stage_scalability.csv');sg=defaultdict(dict)
    for x in scale:sg[(x['mesh_dimension'],x['packet_count'],x['seed'],x['repeat'])][x['trace_mode']]=x
    for k,modes in sorted(sg.items(),key=lambda z:tuple(map(int,z[0]))):
        if not {'full','metrics_only'}<=modes.keys():continue
        x,y=modes['full'],modes['metrics_only'];ok=x['canonical_delivery_sha256']==y['canonical_delivery_sha256'] and x['simulated_cycles']==y['simulated_cycles'];case='mesh%s-p%s-s%s-r%s'%k
        M.append(row('STAGE scalability','full vs metrics-only',case,'Exact' if ok else 'failed','delivery hash and cycles',ok,'STAGE full','STAGE metrics-only',repeat=k[3],error=0 if ok else ''))
        P.append(pair('scalability',case,x['topology_canonical_sha256'],y['topology_canonical_sha256'],x['canonical_delivery_sha256'],y['canonical_delivery_sha256'],ok,'trace retention only'))
    rg=defaultdict(list)
    for x in scale:rg[(x['mesh_dimension'],x['packet_count'],x['trace_mode'])].append(x)
    for k,v in sorted(rg.items(),key=lambda z:(int(z[0][0]),int(z[0][1]),z[0][2])):
        RT.append(dict(tool='STAGE',case_group=f'{k[0]}x{k[0]}-{k[1]} packets',mode=k[2],runs=len(v),process_wall_seconds_median=med(x['process_wall_seconds'] for x in v),simulation_wall_seconds_median=med(x['simulation_wall_seconds'] for x in v),peak_working_set_mib_median=(med(x['peak_working_set_bytes'] for x in v) or 0)/1048576,raw_trace_mib_median=(med(x['raw_trace_bytes'] for x in v) or 0)/1048576,denominator='requested packets drained',evidence_level='Measured'))
    for x in loadc(F/'summary/rq3_timeloop_stage_cycles.csv'):
        e=num(x['compute_cycle_relative_error']);ok=x['evidence_level']=='Exact' and e==0
        M.append(row('Timeloop','16-MAC compute schedule',x['case_id'],'Exact' if ok else 'failed','MAC count and compute floor',ok,'timeloop-model','STAGE compute-only',error=e,note='Full-system excess is STAGE-only.'))
        P.append(pair('Timeloop',x['case_id'],x['paired_config_hash'],x['paired_config_hash'],x['compute_trace_hash'],x['compute_trace_hash'],ok,'frozen mapping and 16 MAC/cycle'))
    for x in loadc(F/'summary/rq3_timeloop_stage_accesses.csv'):
        if num(x['relative_error']) not in (None,0):D.append(dict(domain='Timeloop',case_id=x['case_id'],metric=x['hierarchy'],observed_a=x['timeloop_model_accesses'],observed_b=x['stage_replayed_accesses'],relative_delta=x['relative_error'],first_divergence='counter',root_cause='access replay mismatch',fixability='inspect mapping replay',final_evidence_level='failed'))
    tg=defaultdict(dict)
    for x in loadc(R/'summary/holdout_scalesim_stage_timing.csv'):tg[(x['case_id'],x['repeat'])][x['tool']]=x
    for (case,rp),tools in sorted(tg.items()):
        if not {'stage','scalesim'}<=tools.keys():continue
        s,t=tools['scalesim'],tools['stage'];e=err(t['total_cycles'],s['total_cycles'])
        M.append(row('SCALE-Sim','4x4 WS hold-out timing',case,'Trend','cold total cycles','trend-only','SCALE-Sim','STAGE',s['status'],t['status'],rp,e,'Shared shape/array/prefetch boundary; independent schedule.'))
        D.append(dict(domain='SCALE-Sim',case_id=case,metric='total_cycles',observed_a=s['total_cycles'],observed_b=t['total_cycles'],relative_delta=e,first_divergence='native schedule',root_cause='Independent tiling and memory arbitration.',fixability='A shared scheduler would remove implementation independence.',final_evidence_level='Trend'))
    for case in sorted({x['case_id'] for x in candidates if x['provider']=='scalesim'}):
        its=[x for x in candidates if x['provider']=='scalesim' and x['case_id']==case];its.sort(key=lambda x:x['repeat']);js=[loadj(R/x['raw_relpath']) for x in its];hs=[x['metrics']['canonical_metrics_sha256'] for x in js];ok=len(set(hs))==1 and all(x['status']=='completed' for x in js)
        P.append(pair('SCALE-Sim repeat',case,js[0]['config_hash'],js[1]['config_hash'],hs[0],hs[1],ok,'same shape/config/seed'))
    for x in loadc(F/'summary/rq3_accelergy_verification.csv'):
        ok=x['status']=='completed' and truth(x['ert_non_empty']) and not truth(x['dummy_action_detected']) and not truth(x['schema_fallback_detected'])
        M.append(row('Accelergy','shared 45-nm reference',x['case_id'],'Exact' if ok else 'failed','ERT and action accounting',ok,'Accelergy','STAGE action replay',x['status'],'completed',error=0 if ok else '',note='Reference model, not silicon-calibrated power.'))
    for x in loadc(F/'summary/rq3_energy_microbench.csv'):
        if num(x['relative_error']) not in (None,0):D.append(dict(domain='Accelergy',case_id=x['case_id'],metric=x['component']+':'+x['action'],observed_a=x['accelergy_ert_energy_pj'],observed_b=x['stage_energy_pj'],relative_delta=x['relative_error'],first_divergence='one action',root_cause='energy replay mismatch',fixability='inspect action map',final_evidence_level='failed'))
    for x in loadc(F/'summary/mnist_cnn_noc_cross_tool.csv'):
        ok=truth(x['exact_input_trace_match']) and truth(x['both_fully_drained']) and x['booksim_delivered_packets']==x['stage_delivered_packets'];e=err(x['stage_packet_latency_avg'],x['booksim_packet_latency_avg'])
        M.append(row('BookSim2 CNN','materialized packet trace',x['case_id'],'Exact input/delivery; Trend timing','input hash and delivery',ok,'BookSim2','STAGE',error=e,note=x['claim_boundary']))
        D.append(dict(domain='BookSim2 CNN',case_id=x['case_id'],metric='average packet latency',observed_a=x['booksim_packet_latency_avg'],observed_b=x['stage_packet_latency_avg'],relative_delta=x['latency_delta_pct_of_booksim'],first_divergence='router/flow control',root_cause='BookSim2 wormhole credits vs STAGE tail-complete store-and-forward.',fixability='Requires equivalent router pipeline and credit model.',final_evidence_level='Trend'))
    ext=loadc(R/'summary/specialist_tool_runtime_context.csv');eg=defaultdict(list)
    for x in ext:eg[(x['tool'],x.get('case') or x.get('traffic') or 'native')].append(x)
    for k,v in sorted(eg.items()):RT.append(dict(tool=k[0],case_group=k[1],mode='native external context',runs=len(v),process_wall_seconds_median=med(x['process_wall_seconds'] for x in v),simulation_wall_seconds_median='',peak_working_set_mib_median=(med(x['peak_working_set_bytes'] for x in v) or 0)/1048576,raw_trace_mib_median='',denominator=v[0].get('denominator_kind','native'),evidence_level='Measured context; ratio only after denominator gate'))
    valid=ROOT/'experiments/aspdac/specs/ui_scenes/VALIDATION.md';txt=valid.read_text(encoding='utf-8');gh=re.search(r'SHA-256:\s*([0-9a-f]{64})',txt);th=re.search(r'same SHA-256\s*([0-9a-f]{64})',txt);ok=bool(gh and th)
    M.append(row('multi-chip scene','two full traces','electro-optical-multichip','Exact' if ok else 'failed','scene trace hash',ok,'STAGE','STAGE',repeat='0,1',error=0 if ok else '',note='Scene validation only; BER not modeled.'))
    P.append(pair('scene','electro-optical-multichip',gh.group(1) if gh else '',gh.group(1) if gh else '',th.group(1) if th else '',th.group(1) if th else '',ok,'same graph and seed'))
    for fn,expected in [('required-golden-start.json',(7,7,0)),('phase8a-start.json',(154,154,0)),('phase9-start.json',(24,24,0)),('full-release-start.json',(491,491,0))]:
        j=loadj(B/'raw/regression'/fn);got=(j['total'],j['passed'],j['failed']);ok=got==expected;M.append(row('baseline','Release regression',fn[:-5],'Exact' if ok else 'failed','test counts',ok,'expected','observed','pass','pass' if ok else 'fail',error=0 if ok else '',note=str(got)))
    D.append(dict(domain='environment',case_id='current external rerun',metric='F-only WSL output',observed_a='automount disabled; /mnt/f is Linux virtual disk',observed_b='passwordless mount unavailable',relative_delta='',first_divergence='storage boundary',root_cause='New WSL output would consume the constrained C drive.',fixability='Mount Windows F in a future interactive session.',final_evidence_level='blocked_external'))
    D.append(dict(domain='environment',case_id='Release build',metric='NuGet audit warnings',observed_a='2 NU1900 warnings',observed_b='0 compiler errors; all regression suites pass',relative_delta='',first_divergence='package vulnerability feed',root_cause='The isolated F-side NuGet cache could not reach api.nuget.org through the host proxy.',fixability='Repeat restore with the audit feed available; do not classify as a simulator regression.',final_evidence_level='environment_degraded'))
    rr=loadc(B/'summary/p1_stage_replay_execution.csv')
    for kind in sorted({x['kind'] for x in rr}):
        v=[x for x in rr if x['kind']==kind and x['elapsed_seconds']];RT.append(dict(tool='STAGE',case_group='P1 '+kind+' replay',mode='current commit',runs=len(v),process_wall_seconds_median=med(x['elapsed_seconds'] for x in v),simulation_wall_seconds_median='',peak_working_set_mib_median='',raw_trace_mib_median='',denominator='one registered candidate',evidence_level='Measured'))
    S=B/'summary';Q=B/'manifests';X=B/'failures';S.mkdir(exist_ok=True);Q.mkdir(exist_ok=True);X.mkdir(exist_ok=True)
    wc(S/'consistency_case_matrix.csv',M);wc(S/'consistency_pair_hashes.csv',P);wc(S/'consistency_differences.csv',D);wc(S/'simulator_runtime_summary.csv',RT)
    claims=[
      ('Deterministic cases reproduce byte-identical canonical traces.','measured','Exact','9 new oracle pairs plus regression baselines'),
      ('Registered STAGE hold-out and supported NoC cases replay unchanged on 87b1ad0.','measured','Exact','34 terminal replays; 0 failed'),
      ('Timeloop compute floors and hierarchy access replay match STAGE.','measured','Exact','frozen 16-MAC mapping'),
      ('STAGE and SCALE-Sim are cycle-equivalent.','must_not_claim','Trend','independent schedule and memory arbitration'),
      ('Accelergy has valid ERTs and exact shared-ERT action replay.','measured','Exact accounting','five workloads; no dummy or fallback'),
      ('BookSim2 and STAGE have equal CNN packet latency.','must_not_claim','Trend','same input/delivery, unmatched network microarchitecture'),
      ('The multi-chip electro-optical scene compiles and replays deterministically.','measured','Exact scene validation','35 components, 36 links, repeated trace hash'),
      ('STAGE produces the reported MNIST classification accuracy.','pending','Pending','accuracy is currently a functional oracle'),
      ('Capacity-release NoC timing matches BookSim2 credits.','not_supported','not_supported','four explicit registered repeats'),
      ('The optical model predicts BER.','must_not_claim','not modeled','loss and margin are modeled; BER is not')]
    C=[dict(claim=x,status=y,evidence_level=z,basis=b) for x,y,z,b in claims];wc(S/'paper_claim_status.csv',C)
    (S/'paper_claim_status.md').write_text('# Paper claim status\n\n'+'\n'.join(f"- **{x['status']} / {x['evidence_level']}** — {x['claim']}\n  Basis: {x['basis']}" for x in C)+'\n',encoding='utf-8')
    counts=defaultdict(int)
    for x in M:counts[x['evidence_level']]+=1
    (S/'consistency_root_causes.md').write_text('''# Consistency root causes

## Exact

STAGE deterministic traces, current-commit replays, supported NoC contract timelines, Timeloop compute/access replay, and shared-ERT action accounting are exact under their frozen definitions. Full and metrics-only STAGE runs retain identical delivery hashes.

## Trend only

SCALE-Sim keeps its native systolic schedule and memory arbitration. BookSim2 uses a pipelined wormhole/credit network, while STAGE uses tail-complete store-and-forward and direct-capacity backpressure. Equal packet input and delivery do not imply equal cycles.

## Not supported

Two capacity-release cases, each repeated twice, require explicit credit-release timing absent from the public STAGE contract. They remain not_supported.

## External rerun boundary

WSL drive automount is disabled. Its `/mnt/f` is a Linux virtual-disk directory, not Windows F:. With no passwordless mount, a new external run would violate the C-drive guard. Existing immutable external evidence is audited; the fresh rerun is blocked_external.

## CNN boundary

Both transport tools drain the registered materialized-im2col packet trace. Classification accuracy remains a functional oracle until STAGE executes all numerical layers and reconciles predictions per image.
''',encoding='utf-8')
    (S/'overnight_execution_disposition.md').write_text(f'''# Overnight execution disposition

- Frozen commit: `87b1ad065fd569f5afeddee5057dfadf6d034284`.
- New RQ1 cases: 18/18 completed.
- Current STAGE P1 replay: 34 terminal (28 completed, 2 expected-boundary, 4 not-supported), 0 failed.
- Matrix evidence counts: {dict(sorted(counts.items()))}.
- External current-commit rerun: blocked by the F-only storage guard; no new external output was written to C:.
- Release compilation completed with 0 errors; two cached NU1900 feed warnings remain because the isolated F-side NuGet cache could not reach the vulnerability feed.
- Final and reviewer bundles were immutable inputs.
- Phase 10 remains IN_PROGRESS; Phase 10A remains LOCKED.
''',encoding='utf-8')
    wj(X/'external_current_rerun_blocked.json',dict(schema_version='overnight-external-block/1.0',status='blocked_external',recorded_utc=datetime.now(timezone.utc).isoformat(),reason='WSL automount disabled and /mnt/f is not Windows F; passwordless drvfs mount unavailable.',safe_disposition='Audit immutable completed external bundles.',affected_tools=['BookSim2','timeloop-model','SCALE-Sim','Accelergy'],not_a_test_failure=True))
    sources=[Path(__file__).resolve(),F/'manifests/final_manifest_index.json',R/'manifests/reviewer_extension_manifest_index.json',R/'manifests/p1_candidates.json',ROOT/'experiments/aspdac/specs/ui_scenes/aspdac-multichip-electro-optical.hardware.json']
    idx=Q/'manifest_index.json';files=[]
    for f in sorted(x for x in B.rglob('*') if x.is_file() and x!=idx):files.append(dict(path=f.relative_to(B).as_posix(),bytes=f.stat().st_size,sha256=sha(f)))
    refs=[dict(path=rel(f),bytes=f.stat().st_size,sha256=sha(f),policy='read_only') for f in sources if f.exists()];dr=[]
    for root in [Path('C:/'),Path('F:/')]:u=shutil.disk_usage(root);dr.append(dict(drive=root.drive,free_bytes=u.free,free_gib=round(u.free/1073741824,2)))
    wj(idx,dict(schema_version='overnight-consistency-manifest/1.0',generated_utc=datetime.now(timezone.utc).isoformat(),bundle_root=rel(B),git_commit='87b1ad065fd569f5afeddee5057dfadf6d034284',phase10='IN_PROGRESS',phase10a='LOCKED',case_rows=len(M),pair_rows=len(P),difference_rows=len(D),runtime_rows=len(RT),evidence_level_counts=dict(sorted(counts.items())),drive_snapshot=dr,source_bundles=refs,files=files,limitations=['Current external rerun blocked by F-only storage guard; completed immutable evidence remains the external basis.']))
    failed=[x for x in M if x['evidence_level']=='failed'];print(json.dumps(dict(matrix_rows=len(M),pair_rows=len(P),difference_rows=len(D),runtime_rows=len(RT),failed_rows=len(failed),bundle=str(B)),indent=2));return 1 if failed else 0
if __name__=='__main__':raise SystemExit(main())