namespace ShaderCompilation {
constexpr uint32_t Flags=D3DCOMPILE_ENABLE_STRICTNESS|D3DCOMPILE_WARNINGS_ARE_ERRORS|D3DCOMPILE_OPTIMIZATION_LEVEL3;
uint32_t DiagnosticFailure(size_t diagnosticBytes,NcmaErrorV1* error){
    NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT,"shader_compile_failed; diagnostics_redacted");
    error->reserved=diagnosticBytes>16384?1u:0u;error->required_bytes=static_cast<uint32_t>(std::min<size_t>(diagnosticBytes,UINT32_MAX));return NCMA_INVALID_ARGUMENT;
}
bool Identifier(std::string_view s){
    const auto letter=[](char c){return (c>='a'&&c<='z')||(c>='A'&&c<='Z')||c=='_';};
    if(s.empty()||s.size()>63||!letter(s.front()))return false;
    for(char c:s)if(!letter(c)&&!(c>='0'&&c<='9'))return false;return true;
}
bool Name(char* output,const char* input,bool uppercase=false){
    if(!input)return false;const auto count=strnlen(input,64);if(!Identifier({input,count}))return false;
    for(size_t i=0;i<count;i++){char c=input[i];output[i]=uppercase&&c>='a'&&c<='z'?static_cast<char>(c-'a'+'A'):c;}return true;
}
bool BindingName(char* output,const char* input,uint32_t& index,bool& array){
    if(!input)return false;const auto count=strnlen(input,128);if(count>=128)return false;
    std::string_view name{input,count};const auto open=name.find('[');index=0;array=false;
    if(open==std::string_view::npos)return Name(output,input);
    if(name.back()!=']'||!Identifier(name.substr(0,open)))return false;
    const auto number=name.substr(open+1,name.size()-open-2);const auto parsed=std::from_chars(number.data(),number.data()+number.size(),index);
    if(parsed.ec!=std::errc{}||parsed.ptr!=number.data()+number.size()||index>=64)return false;
    std::memcpy(output,name.data(),open);array=true;return true;
}
bool Scalar(D3D_SHADER_VARIABLE_TYPE type,uint32_t& output){
    if(type==D3D_SVT_FLOAT)output=0;else if(type==D3D_SVT_INT)output=1;else if(type==D3D_SVT_UINT)output=2;else return false;return true;
}
bool Resource(D3D_SHADER_INPUT_TYPE type,D3D_SRV_DIMENSION dimension,uint32_t& output){
    switch(type){
    case D3D_SIT_TEXTURE:if(dimension==D3D_SRV_DIMENSION_TEXTURE2D)output=0;else if(dimension==D3D_SRV_DIMENSION_TEXTURECUBE)output=1;else return false;break;
    case D3D_SIT_STRUCTURED:output=2;break;case D3D_SIT_BYTEADDRESS:output=3;break;case D3D_SIT_SAMPLER:output=4;break;
    case D3D_SIT_UAV_RWSTRUCTURED:output=5;break;case D3D_SIT_UAV_RWBYTEADDRESS:output=6;break;default:return false;
    }return true;
}
// Disassembler consumes compiler-owned bytecode only. Parse exact bounded SM5 declarations,
// not comments/source/NumSamples. Ambiguity/missing/unsupported forms reject, no guessed stride.
bool StructuredStride(std::string_view assembly,uint32_t slot,bool writable,uint32_t& stride){
    const std::string token=writable?"dcl_uav_structured ":"dcl_resource_structured ";bool found=false;
    while(!assembly.empty()){
        const auto end=assembly.find('\n');auto line=assembly.substr(0,end);assembly=end==std::string_view::npos?std::string_view{}:assembly.substr(end+1);
        while(!line.empty()&&(line.front()==' '||line.front()=='\t'))line.remove_prefix(1);
        if(!line.starts_with(token))continue;line.remove_prefix(token.size());if(line.empty()||line.front()!=(writable?'u':'t'))continue;line.remove_prefix(1);
        uint32_t at=0,value=0;auto first=std::from_chars(line.data(),line.data()+line.size(),at);if(first.ec!=std::errc{}||at!=slot)continue;
        line.remove_prefix(static_cast<size_t>(first.ptr-line.data()));while(!line.empty()&&line.front()==' ')line.remove_prefix(1);
        if(line.empty()||line.front()!=',')return false;line.remove_prefix(1);while(!line.empty()&&line.front()==' ')line.remove_prefix(1);
        auto second=std::from_chars(line.data(),line.data()+line.size(),value);if(second.ec!=std::errc{}||value<4||value>2048||value%4||found)return false;
        for(auto p=second.ptr;p!=line.data()+line.size();++p)if(*p!=' '&&*p!='\r')return false;stride=value;found=true;
    }return found;
}
uint32_t Run(const NcmaShaderCompileV1& request,NcmaShaderOutputV1* output,NcmaShaderRowV1* destination,uint32_t capacity,uint8_t* bytecode,uint32_t bytes,NcmaErrorV1* error){
    if(request.struct_size!=56||request.version!=1)return NcmaPlugin::Error(error,NCMA_ABI_MISMATCH);
    if(request.stage>2||request.reserved||request.reserved2||!request.source||!request.source_bytes||request.source_bytes>262144||!request.entry||!request.entry_bytes||request.entry_bytes>63||request.macro_count>16||(request.macro_count&&!request.macros)||!output||!destination||!bytecode||capacity>256||bytes>1048576)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
    const std::string_view source{reinterpret_cast<const char*>(request.source),request.source_bytes};
    const std::string_view entry{request.entry,request.entry_bytes};if(source.find('\0')!=std::string_view::npos||!Identifier(entry))return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
    std::array<D3D_SHADER_MACRO,17> macros{};std::unordered_set<std::string_view> names;
    for(uint32_t i=0;i<request.macro_count;i++){
        const auto& m=request.macros[i];const auto nameBytes=strnlen(m.name,64),valueBytes=strnlen(m.value,64);
        if(!Identifier({m.name,nameBytes})||!valueBytes||valueBytes>=64||!names.insert({m.name,nameBytes}).second)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        for(size_t at=0;at<valueBytes;at++)if(static_cast<unsigned char>(m.value[at])<32||static_cast<unsigned char>(m.value[at])>126)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        macros[i]={m.name,m.value};
    }
    const std::string entryCopy{entry};ComPtr<ID3DBlob> code,diagnostic;
    const char* target=request.stage==0?"vs_5_0":request.stage==1?"ps_5_0":"cs_5_0";
    HRESULT hr=D3DCompile(source.data(),source.size(),"Ncma.TrustedShader.v1",macros.data(),nullptr,entryCopy.c_str(),target,Flags,0,&code,&diagnostic);
    // Do NOT expose compiler diagnostics: #error/#line may contain secrets/project paths/source.
    // Size/truncation is still explicit; actual text is not retained or logged by this service.
    if(FAILED(hr))return DiagnosticFailure(diagnostic?diagnostic->GetBufferSize():0,error);
    if(!code||code->GetBufferSize()>1048576)return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"shader_bytecode_budget");
    ComPtr<ID3D11ShaderReflection> reflection;if(FAILED(D3DReflect(code->GetBufferPointer(),code->GetBufferSize(),__uuidof(ID3D11ShaderReflection),&reflection)))return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR,"shader_reflection_failed");
    D3D11_SHADER_DESC desc{};if(FAILED(reflection->GetDesc(&desc))||desc.InputParameters>256||desc.BoundResources>64||desc.ConstantBuffers>64)return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"shader_reflection_budget");
    const uint32_t shaderType=(desc.Version>>16)&0xffffu;const uint32_t expectedType=request.stage==0?1u:request.stage==1?0u:5u;
    if(shaderType!=expectedType||((desc.Version>>4)&15u)!=5u||(desc.Version&15u)!=0)return NcmaPlugin::Error(error,NCMA_ABI_MISMATCH);
    std::vector<NcmaShaderRowV1> rows;rows.reserve(256);
    const auto append=[&](const NcmaShaderRowV1& row){if(rows.size()==256)return false;rows.push_back(row);return true;};
    if(request.stage==0)for(uint32_t i=0;i<desc.InputParameters;i++){
        D3D11_SIGNATURE_PARAMETER_DESC input{};if(FAILED(reflection->GetInputParameterDesc(i,&input)))return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR);
        NcmaShaderRowV1 row{};row.kind=1;row.stage=request.stage;row.index=input.SemanticIndex;row.columns=static_cast<uint32_t>(std::popcount(static_cast<unsigned int>(input.Mask)));row.offset=UINT32_MAX;
        if(!Name(row.name,input.SemanticName,true)||row.columns<1||row.columns>4)return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"shader_input_type");
        if(input.ComponentType==D3D_REGISTER_COMPONENT_FLOAT32)row.scalar=0;else if(input.ComponentType==D3D_REGISTER_COMPONENT_SINT32)row.scalar=1;else if(input.ComponentType==D3D_REGISTER_COMPONENT_UINT32)row.scalar=2;else return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"shader_input_type");
        if(!append(row))return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"shader_reflection_budget");
    }
    ComPtr<ID3DBlob> assembly;
    for(uint32_t i=0;i<desc.BoundResources;i++){
        D3D11_SHADER_INPUT_BIND_DESC binding{};if(FAILED(reflection->GetResourceBindingDesc(i,&binding)))return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR);
        NcmaShaderRowV1 row{};row.stage=request.stage;row.slot=binding.BindPoint;row.count=binding.BindCount;
        uint32_t arrayIndex=0;bool bindingArray=false;
        if(!(binding.Type==D3D_SIT_CBUFFER?Name(row.name,binding.Name):BindingName(row.name,binding.Name,arrayIndex,bindingArray))||!row.count)return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"shader_binding");
        if(binding.Type==D3D_SIT_CBUFFER){
            if(row.slot>=14||row.count!=1)return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"shader_constant_slot");
            auto* buffer=reflection->GetConstantBufferByName(binding.Name);D3D11_SHADER_BUFFER_DESC bufferDesc{};
            if(!buffer||FAILED(buffer->GetDesc(&bufferDesc))||bufferDesc.Type!=D3D_CT_CBUFFER||bufferDesc.Variables>256||bufferDesc.Size>65536)return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"shader_constant_type");
            row.kind=2;row.bytes=bufferDesc.Size;if(!append(row))return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"shader_reflection_budget");
            for(uint32_t v=0;v<bufferDesc.Variables;v++){
                auto* variable=buffer->GetVariableByIndex(v);D3D11_SHADER_VARIABLE_DESC variableDesc{};D3D11_SHADER_TYPE_DESC type{};
                if(!variable||FAILED(variable->GetDesc(&variableDesc))||FAILED(variable->GetType()->GetDesc(&type)))return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR);
                NcmaShaderRowV1 member{};member.kind=3;member.stage=request.stage;member.slot=row.slot;member.offset=variableDesc.StartOffset;member.bytes=variableDesc.Size;member.rows=type.Rows;member.columns=type.Columns;member.array_count=std::max(1u,type.Elements);
                if(!Name(member.name,variableDesc.Name)||!Name(member.parent,binding.Name)||!Scalar(type.Type,member.scalar)||type.Members||type.Rows<1||type.Rows>4||type.Columns<1||type.Columns>4||member.array_count>4096)return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"shader_member_type");
                if(type.Class==D3D_SVC_MATRIX_ROWS)member.order=1;else if(type.Class==D3D_SVC_MATRIX_COLUMNS)member.order=2;else if(type.Class!=D3D_SVC_SCALAR&&type.Class!=D3D_SVC_VECTOR)return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"shader_member_type");
                const auto element=member.order==1?(member.rows-1)*16+member.columns*4:member.order==2?(member.columns-1)*16+member.rows*4:member.columns*4;
                if(member.array_count>1){if(member.bytes<element||(member.bytes-element)%(member.array_count-1))return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"shader_array_span");member.array_stride=(member.bytes-element)/(member.array_count-1);}
                if(!append(member))return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"shader_reflection_budget");
            }
        }else{
            row.kind=4;if(!Resource(binding.Type,binding.Dimension,row.resource_kind))return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"shader_resource_type");
            if((row.resource_kind<=1&&binding.ReturnType!=D3D_RETURN_TYPE_FLOAT)||(row.resource_kind==4&&(binding.uFlags&D3D_SIF_COMPARISON_SAMPLER)))return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"shader_resource_sample_type");
            row.access=row.resource_kind>=5?1u:0u;
            const auto limit=row.resource_kind==4?16u:row.access?8u:64u;
            if(row.slot>=limit||row.count>limit-row.slot||(row.access&&request.stage!=2))return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"shader_resource_slot");
            if(row.resource_kind==2||row.resource_kind==5){
                if(!assembly&&FAILED(D3DDisassemble(code->GetBufferPointer(),code->GetBufferSize(),0,nullptr,&assembly)))return NcmaPlugin::Error(error,NCMA_INTERNAL_ERROR);
                if(!assembly||assembly->GetBufferSize()>4194304||!StructuredStride({static_cast<const char*>(assembly->GetBufferPointer()),assembly->GetBufferSize()-1},row.slot,row.access!=0,row.stride))return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"shader_structured_stride");
            }
            if(bindingArray&&arrayIndex){
                const auto previous=std::find_if(rows.begin(),rows.end(),[&](const NcmaShaderRowV1& prior){return prior.kind==4&&std::strcmp(prior.name,row.name)==0;});
                if(previous==rows.end()||previous->count!=arrayIndex||previous->slot+previous->count!=row.slot||previous->resource_kind!=row.resource_kind||previous->access!=row.access||previous->stride!=row.stride)return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"shader_binding_array");
                previous->count+=row.count;
            }else if(!append(row))return NcmaPlugin::Error(error,NCMA_UNSUPPORTED_FEATURE,"shader_reflection_budget");
        }
    }
    if(capacity<rows.size()||bytes<code->GetBufferSize()){NcmaPlugin::Error(error,NCMA_BUFFER_TOO_SMALL,"shader_output_capacity");error->required_bytes=static_cast<uint32_t>(code->GetBufferSize());return NCMA_BUFFER_TOO_SMALL;}
    // One final copied publication. Failed compilation/last-row/capacity never touches caller output.
    if(!rows.empty())std::memcpy(destination,rows.data(),rows.size()*sizeof(NcmaShaderRowV1));std::memcpy(bytecode,code->GetBufferPointer(),code->GetBufferSize());
    *output={32,1,request.stage,static_cast<uint32_t>(rows.size()),static_cast<uint32_t>(code->GetBufferSize()),D3D_COMPILER_VERSION,Flags,0};return NCMA_OK;
}
}
uint32_t NCMA_CALL ValidateShaderPreparation(uint64_t context,uint64_t handle,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        const auto valid=Instance(context,handle,error);if(valid)return valid;
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
        if(renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY,"shader_compile_requires_off_frame");return NCMA_OK;
    });
}
uint32_t NCMA_CALL CompileShader(uint64_t context,uint64_t handle,const NcmaShaderCompileV1* request,NcmaShaderOutputV1* output,NcmaShaderRowV1* rows,uint32_t capacity,uint8_t* bytes,uint32_t byteCapacity,NcmaErrorV1* error) noexcept {
    return NcmaPlugin::Guard(error,[&]() -> uint32_t {
        const auto valid=Instance(context,handle,error);if(valid)return valid;
        if(!request)return NcmaPlugin::Error(error,NCMA_INVALID_ARGUMENT);
        if(renderer->failed)return NcmaPlugin::Error(error,renderer->faultResult);
        if(renderer->active)return NcmaPlugin::Error(error,NCMA_BUSY,"shader_compile_requires_off_frame");
        BusyScope busyScope;return ShaderCompilation::Run(*request,output,rows,capacity,bytes,byteCapacity,error);
    });
}
