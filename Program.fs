open System
open System.IO
open FSharp.Control
open FSharp.Data
open FSharp.CommandLine.Commands
open FSharp.CommandLine.Options
open FSharp.CommandLine.OptionValues
open CoreTweet

[<Literal>]
let ContestsUrl = "https://kenkoooo.com/atcoder/atcoder-api/info/contests"
type Contests = JsonProvider<ContestsUrl>

[<Literal>]
let MergedProblemsUrl = "https://kenkoooo.com/atcoder/atcoder-api/info/merged-problems"
type MergedProblems = JsonProvider<MergedProblemsUrl>

let inline getContests () = Contests.AsyncLoad ContestsUrl

let inline getMergedProblems () =
  async {
    let! data = MergedProblems.AsyncLoad MergedProblemsUrl
    let! raw  = JsonValue.AsyncLoad MergedProblemsUrl
    return data, raw.ToString(JsonSaveOptions.None)
  }

type TestCase = {
  caseName: string
  status:   string
  execTime: TimeSpan
  memory:   int
}

type Submission = {
  url: string
  id: string
  sourceCode: string
  info: Map<string, string>
  testCases: TestCase list
}

let getSubmission url =
  async {
    let! page = HtmlDocument.AsyncLoad url

    let id =
      page.Descendants ["span"]
      |> Seq.filter (fun x -> x.HasClass("h2"))
      |> Seq.map    (fun x -> x.InnerText())
      |> Seq.find   (String.startsWith "Submission #")
      |> String.splitBy '#' |> Seq.item 1

    let src =
      page.Descendants ["pre"]
      |> Seq.find (fun x -> x.HasId "submission-code")
      |> HtmlNodeExtensions.InnerText

    let sub_info :: tc_summary :: tc_data :: _ = page.Descendants ["table"] |> List.ofSeq
    
    let info =
      seq {
        for tr in sub_info.Descendants ["tr"] do
          let th = tr.Descendants["th"] |> Seq.head
          let td = tr.Descendants["td"] |> Seq.head
          yield 
            th.InnerText() |> String.replace ' ' '_' |> String.toLower None,
            td.InnerText() |> String.trim
      } |> Map.ofSeq
    
    let tcases =
      [
        for tr in (tc_data.Descendants["tbody"] |> Seq.head).Descendants ["tr"] do
          let td = tr.Descendants["td"] |> Array.ofSeq
          yield {
            caseName = td.[0].InnerText()
            status   = td.[1].InnerText()
            execTime = td.[2].InnerText() |> String.splitBy " ms"
                       |> Seq.head |> float |> TimeSpan.FromMilliseconds
            memory   = td.[3].InnerText() |> String.splitBy " KB"
                       |> Seq.head |> int
          }
      ]

    return { url = url; id = id; sourceCode = src; info = info; testCases = tcases }
  }

let inline strarg name = 
  commandOption { names [name]; description ""; takes (format "%s") }

let inline flag name =
  commandFlag { names [name]; description "" }

let mainCmd =
  command {
    name "atgolfer"
    description "https://twitter.com/atgolfer1"

    opt post in flag "post" |> CommandOption.whenMissingUse false
    opt ck in strarg "consumer-key"
    opt cs in strarg "consumer-secret"
    opt atk in strarg "access-token-key"
    opt ats in strarg "access-token-secret"
    opt store in strarg "store"
    opt nostore in flag "nostore" |> CommandOption.map (Option.map not)
    opt load in strarg "load"
    opt noload in flag "noload"   |> CommandOption.map (Option.map not)

    let doesStore = nostore ?| store.IsSome
    let doesLoad  = noload  ?| load.IsSome

    let tokens =
      match post, ck, cs, atk, ats with
        | false, _, _, _, _ -> None
        | true, Some k, Some s, Some tk, Some ts ->
          Tokens.Create (k, s, tk, ts) |> Some
        | _ ->
          failwith "all of --{consumer,access-token}-{key,secret} are required if --post is used"
    
    async {
      let! contests = getContests()
      let! mergedProblems, mpjson = getMergedProblems()

      let lastMergedProblems =
        if doesLoad && File.Exists load.Value then
          File.ReadAllText load.Value |> MergedProblems.Parse
        else Array.empty

      if doesStore then
        let dir = Path.GetDirectoryName store.Value
        Directory.CreateDirectory dir |> ignore
        File.WriteAllText (store.Value, mpjson)
      
      let inline contestFromId id =
        contests |> Seq.find (fun c -> c.Id = id)
      let inline lastProblemFromId id = 
        lastMergedProblems |> Seq.tryFind (fun p -> p.Id = id)

      for problem in mergedProblems |> Seq.filter (fun p -> p.SolverCount > 0) do
        let lastProblem      = problem.Id  |> lastProblemFromId
        let lastSubmissionId = lastProblem |> Option.bind (fun p -> p.ShortestSubmissionId)

        if   problem.ShortestContestId.IsSome
          && problem.ShortestSubmissionId.IsSome
          && lastSubmissionId |> Option.map ((<>) problem.ShortestSubmissionId.Value) ?| false then
          let shortestContestId = problem.ShortestContestId.Value
          let shortestSubmissionId = problem.ShortestSubmissionId.Value
          let url = sprintf "https://beta.atcoder.jp/contests/%s/submissions/%i"
                            shortestContestId
                            shortestSubmissionId
          let! newSubmission = getSubmission url

          let contestTitle = (contestFromId shortestContestId).Title
          let problemTitle = problem.Title
          let newUser      = problem.ShortestUserId.JsonValue.ToString()
          let newSize      = newSubmission.info |> Map.find "code_size"
          
          let! textBody =
            if lastSubmissionId.IsSome then
              async {
                let! oldSubmission =
                  getSubmission <|
                    sprintf "https://beta.atcoder.jp/contests/%s/submissions/%i"
                            lastProblem.Value.ShortestContestId.Value
                            lastSubmissionId.Value
                let oldUser = lastProblem.Value.ShortestUserId.JsonValue.ToString()
                let oldSize = oldSubmission.info |> Map.find "code_size"
                if newUser = oldUser then
                  return sprintf "%s さんが自身のショートコードを更新しました！ (%s → %s)" 
                                 newUser                               oldSize newSize
                else
                  return sprintf "%s さんが %s さんからショートコードを奪取しました！ (%s → %s)"
                                 newUser oldUser                            oldSize newSize
              }
            else
              Async.returnValue <|
                sprintf "%s さんがショートコードを打ち立てました！ (%s)"
                        newUser                            newSize
          let text =
            sprintf "%s: %s\n%s\n%s" contestTitle problemTitle textBody url
          eprintfn "%s" text

          if tokens.IsSome then
            do! tokens.Value.Statuses.UpdateAsync text
                |> Async.AwaitTask |> Async.Ignore

          do! Async.Sleep 3000 
    } |> Async.run
    return 0
  }

[<EntryPoint>]
let main argv =
  mainCmd |> Command.runAsEntryPoint argv