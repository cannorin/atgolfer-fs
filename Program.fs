open System
open System.IO
open System.Text
open FSharp.Control
open FSharp.Data
open FSharp.CommandLine.Commands
open FSharp.CommandLine.Options
open FSharp.CommandLine.OptionValues
open CoreTweet

[<Literal>]
let ContestsUrl = "https://kenkoooo.com/atcoder/atcoder-api/info/contests"
type Contests = JsonProvider<ContestsUrl, Encoding="UTF-8">

[<Literal>]
let MergedProblemsUrl = "https://kenkoooo.com/atcoder/atcoder-api/info/merged-problems"
type MergedProblems = JsonProvider<MergedProblemsUrl, Encoding="UTF-8">

let inline getContests () = Contests.AsyncLoad ContestsUrl

let inline getMergedProblems () =
  async {
    let! data = MergedProblems.AsyncLoad MergedProblemsUrl
    let! raw  = JsonValue.AsyncLoad MergedProblemsUrl
    return data, raw.ToString(JsonSaveOptions.None)
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
          let contestTitle = (contestFromId shortestContestId).Title
          let problemTitle = problem.Title
          let newUser      = problem.ShortestUserId.JsonValue.InnerText()
          let newSize      = problem.SourceCodeLength.Value
          
          let textBody =
            if lastSubmissionId.IsSome then
              let oldUser = lastProblem.Value.ShortestUserId.JsonValue.InnerText()
              let oldSize = lastProblem.Value.SourceCodeLength.Value
              if newUser = oldUser then
                sprintf "%s さんが自身のショートコードを更新しました！ (%i → %i)" 
                        newUser                               oldSize newSize
              else
                sprintf "%s さんが %s さんからショートコードを奪取しました！ (%i → %i)"
                        newUser oldUser                            oldSize newSize
            else
              sprintf "%s さんがショートコードを打ち立てました！ (%i)"
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