parser grammar ExampleParser;
options {
    tokenVocab = ExampleLexer;
    language = CSharp;
}

program : stat* EOF ;

stat : ID '=' expr ';' 	    #assignmentStatement
    | 'var' ID '=' expr ';' #varDeclStatement
    | expr ';'              #exprStatement
    | 'return' expr? ';'    #returnStatement
    | fnDefinition          #fnDefStatement
    //| 'if' cond+=expr then+=block ('elif' cond+=expr then+=block)* ('else' else=block)? #ifStatement
    //| block               #blockStatement
    ;

fnDefinition : public_='public'? 'function' ID '(' parameterList ')' (fnBody | ';') ;

fnBody : '{' '}' ;
//fnBody : '{' stat* '}' ;

parameter : ID ;

parameterList : (parameter (',' parameter)*)? ;

block : '{' stat* '}' ;

expr : ID                   #idExpr
    | INT                   #intExpr
    | STRING_LITERAL        #strExpr
    | CHAR_LITERAL          #charLitExpr
    // | target=expr '(' (args+=expr (',' args+=expr)*)? ')'  #funcCallExpr
    // | expr '+' expr         #addExpr
    // | 'not' expr            #notExpr
    // | expr 'and' expr       #andExpr
    // | expr 'or' expr        #orExpr
    // | expr 'if' expr        #conditionalExpr
    ;
