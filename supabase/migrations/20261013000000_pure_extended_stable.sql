-- Promote the existing Pure feature grammar to stable. Preserve all RPC
-- permissions and legacy LR0/LR1 beta configuration codes.
do $migration$
declare source text; updated text;
 old_gate constant text := $old$if 'PB1'=any(parts) and (parts[2]<>'BETA' or not ('PP1'=any(parts)) or 'DATA'=any(parts)) then return false; end if;$old$;
 new_gate constant text := $new$if 'PB1'=any(parts) and (not ('PP1'=any(parts)) or 'DATA'=any(parts)) then return false; end if;$new$;
begin
 source:=pg_get_functiondef('paw_private.valid_social_configuration(text)'::regprocedure);
 if (length(source)-length(replace(source,old_gate,'')))<>length(old_gate)
 then raise exception 'Unexpected Pure configuration gate'; end if;
 updated:=replace(source,old_gate,new_gate);
 execute updated;
 if not paw_private.valid_social_configuration('PAW-STABLE-VANILLA-PP1-PB1-AI1-LR1-IW1-SP4-RM1')
 or not paw_private.valid_social_configuration('PAW-STABLE-IMMORTALS-PP1-PB1-AI0-LR1-IW0-SP1-RM0')
 or not paw_private.valid_social_configuration('PAW-BETA-VANILLA-PP1-PB1-AI1-LR0-IW0-SP2-RM1')
 or paw_private.valid_social_configuration('PAW-STABLE-VANILLA-PB1-AI1-LR1-IW1-SP4-RM1')
 or paw_private.valid_social_configuration('PAW-STABLE-IMMORTALS-PP1-PB1-AI1-LR1-IW1-SP4-RM1-DATA')
 then raise exception 'Invalid Pure stable configuration contract'; end if;
end; $migration$;
